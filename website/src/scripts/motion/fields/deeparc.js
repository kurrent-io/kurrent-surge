/* Deep Arc — discharge through a volume rather than across a plane.

   Strikes leave the "Surge" wordmark and branch away from the camera into
   depth, so a bolt recedes as well as travels. Readers are targets set at
   increasing distance, and a widening backlog throws the discharge further
   back into the volume. Nothing stands on screen between strikes: no ticks,
   no ground marks, no head mark. The field is arcs and nothing else. */

import * as THREE from "three";
import { clamp, lerp, mulberry32 } from "../field-kit.js";

export const meta = { id: "deeparc", title: "Deep Arc", engine: "three" };

const FOV = 42;
const CAM_Z = 520;
const ORIGIN_Z = 90; // the wordmark's plane, slightly toward the camera
const DEPTH_FAR = -620;

const MAX_SEGMENTS = 1100;

/* Growth. Steps are world units and the rate is per second, so a strike takes
   the same time on any refresh rate. */
const STEP = 17; // shorter steps, so a channel reads as jagged rather than drawn
const STEPS_PER_SEC = 250;
const JITTER = 0.66; // radians of wander per step
const KINK_P = 0.16; // chance of a hard direction break — the lightning tell
const KINK = 0.85; // radians of that break
const PULL = 0.055; // per-step bend toward the target
const BRANCH_P = 0.24;
/* A branch lives less than half as long as its parent and forks far less
   readily. Real lightning tapers: the fork count per unit of channel falls
   with every generation, or the strike opens into a fan instead of a bolt. */
const GEN_FALLOFF = 0.44;
const MAX_GEN = 3;
const BRANCH_GEN_DECAY = 0.4; // fork chance multiplier per generation
const BRANCH_SPREAD = 0.55; // radians a fork may leave its parent by
const MAX_TIPS = 26; // hard stop; a bad seed must not open into a curtain

/* A Tesla coil throws several streamers from one electrode at once, and real
   lightning strikes the same channel two or three times in quick succession.
   One smooth bolt reads as neither. */
/* Lightning descends. Both are radians either side of straight down: the
   launch spread, and the hard limit a channel may ever wander to. 1.0 rad is
   57 degrees, so a bolt can lean hard but can never run sideways or climb. */
const LAUNCH_TILT = 0.45;
const MAX_TILT = 1.0;
/* Screen pixels above the viewport's top edge where a bolt is born. */
const SKY_MARGIN = 90;
const SKY_SPREAD = 2.2; // multiples of the wordmark half-width
const MAX_PITCH = 0.92; // radians off the XY plane; cos(0.92) = 0.61

const STREAMERS_MIN = 3;
const STREAMERS_MAX = 6;
const RESTRIKE_MAX = 2;
const RESTRIKE_GAP_MS = 70;
const RESTRIKE_VAR_MS = 90;

/* Continuous corona off the letterforms between strikes, so the electrode is
   never inert. Short, faint, and always dying. */
const CORONA_PER_SEC = 7;
const CORONA_LIFE = 9;

/* Lifecycle. The hold is the whole difference between a discharge and a dot
   travelling along a dim line: without it a segment starts decaying the
   instant it is drawn, so only the leading few are ever near full strength
   and the eye never sees the branching structure at all. */
const RISE_MS = 30;
const HOLD_MS = 320;
const QUENCH_MS = 170;
const AFTERGLOW = 0.2; // fraction of peak the quench settles onto
const TAIL_MS = 620; // afterglow decay
const CULL = 0.03;

const CRAWL_HOLD_MS = 860; // the letterforms stay lit after the bolt is gone
const CRAWL_STEPS = 14;
const CRAWL_PAD = 8; // world units outside the glyph box

const STRIKE_MIN_MS = 1150;
const STRIKE_VAR_MS = 1350;

/* DESIGN.md records 0.45 as the per-element ceiling. A live discharge breaks
   it deliberately: the reviewer has asked three times for more, and a bolt at
   the same alpha as a resting line is not a bolt. The corona and the afterglow
   stay well under it, so the field's quiet state still obeys the system. This
   is one constant to revert. */
const A_PEAK = 0.78;
const A_CORONA = 0.3;
const DEPTH_DIM = 0.42; // alpha multiplier at the far plane

const DRIFT_SPEED = 0.09;

/* The glow is not a wider line — a wide line needs LineSegments2, which caps
   every segment with a disc, and those discs read as dots at each kink. It is
   instead two extra 1px copies of the channel, offset perpendicular to it in
   screen space at low alpha, which fringes the bolt without capping anything.
   Offsets are in world units at the focal plane and scaled by distance, so the
   fringe stays the same width on screen at any depth. */
const GLOW_SPREAD = 3.4;
/* Only segments still at or near full strength glow, so the halo belongs to
   the strike and never to the fading afterimage. */
const GLOW_FLOOR = 0.55;

/* Four knobs, not eight. The four that were cut — streamer count, hold time,
   corona rate, branch depth — were the ones that had to be got right together
   before the field read as lightning at all, so they are now fixed at the
   values that do. What remains is what a viewer actually wants to move. */
export const params = [
  {
    key: "gap",
    label: "Seconds between",
    min: 1.5,
    max: 14,
    step: 0.5,
    value: 5.5,
    hint: "Average seconds between bolts. This is the cadence knob: real lightning is rare and the silence is what makes a strike land. Under about three seconds it stops reading as lightning and starts reading as a machine.",
  },
  {
    key: "peak",
    label: "Bolt brightness",
    min: 0.2,
    max: 1,
    step: 0.02,
    value: A_PEAK,
    hint: "Brightness of a live bolt. DESIGN.md records 0.45 as the system's per-element limit — above it is a deliberate deviation.",
  },
  {
    key: "kink",
    label: "Jaggedness",
    min: 0,
    max: 0.6,
    step: 0.02,
    value: KINK_P,
    hint: "Chance per step of a hard direction break. This is the lightning tell — without it, smooth wander reads as a drawn curve rather than a discharge.",
  },
  {
    key: "branch",
    label: "Branching",
    min: 0,
    max: 0.3,
    step: 0.01,
    value: 0.06,
    hint: "Chance PER STEP that a channel forks, so small numbers go a long way — a trunk takes about a hundred steps. Each generation forks at 40% of its parent's rate and lives under half as long, which is what stops a bolt opening into a curtain.",
  },
  {
    key: "glow",
    label: "Strike glow",
    min: 0,
    max: 1.2,
    step: 0.05,
    value: 0.45,
    hint: "A soft halo behind a live bolt only, never behind its afterglow. DESIGN.md bans glow outright, so this is a deliberate exception — at 0 the field is back inside the system.",
  },
  {
    key: "reach",
    label: "Reach",
    min: 0.4,
    max: 3,
    step: 0.1,
    value: 1.35,
    hint: "How far a bolt travels from the wordmark and how deep into the volume it recedes. Higher makes a strike cross more of the page.",
  },
];

/* Fixed at the values that make it read as lightning rather than as a coil.
   A real bolt is one or two channels struck two or three times in quick
   succession, then nothing. Six simultaneous streamers and a continuous corona
   are a Tesla coil — impressive, but a permanent hum, and the hum is what was
   stopping the cadence from reading. */
const FIXED = {
  streamers: 2,
  hold: 260,
  corona: 0,
};

export function create(setup) {
  const { canvas, colors, log } = setup;

  const tune = { ...FIXED };
  for (const p of params) tune[p.key] = p.value;
  let width = setup.width;
  let height = setup.height;
  let dpr = setup.dpr;

  const renderer = new THREE.WebGLRenderer({
    canvas,
    alpha: true,
    antialias: true,
    powerPreference: "low-power",
  });
  renderer.setPixelRatio(dpr);
  renderer.setSize(width, height, false);
  renderer.setClearAlpha(0);

  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(FOV, width / height, 10, 4000);
  camera.position.set(0, 0, CAM_Z);
  camera.lookAt(0, 0, 0);

  const accent = new THREE.Color(colors.accent);
  const fill = new THREE.Color(colors.fill);

  /* Three draws per segment at most: the crisp core plus two fringe copies. */
  const MAX_DRAW = MAX_SEGMENTS * 3;
  const positions = new Float32Array(MAX_DRAW * 6);
  const colorArray = new Float32Array(MAX_DRAW * 8);

  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));
  // Four components so alpha travels per vertex. Scaling RGB toward black
  // instead would paint dark lines over the page, not fade them out.
  geometry.setAttribute("color", new THREE.BufferAttribute(colorArray, 4));
  geometry.setDrawRange(0, 0);

  const material = new THREE.LineBasicMaterial({
    vertexColors: true,
    transparent: true,
    depthWrite: false,
  });
  const lines = new THREE.LineSegments(geometry, material);
  lines.frustumCulled = false;
  scene.add(lines);

  /* Scratch vectors for the glow fringe. Allocating these per segment would
     be a few thousand Vector3 per frame during a strike. */
  const gA = new THREE.Vector3();
  const gB = new THREE.Vector3();
  const gDir = new THREE.Vector3();
  const gToCam = new THREE.Vector3();
  const gPerp = new THREE.Vector3();

  const rand = mulberry32(0x1f3d5b);

  /* A segment is eight numbers, flat: two endpoints, birth time, and a flag
     pair. An array of objects here allocates a few hundred per strike and the
     GC pauses show up as stutter in the middle of a discharge. */
  const SEG = 10;
  const segs = new Float32Array(MAX_SEGMENTS * SEG);
  let segCount = 0;

  let tips = [];
  let restrikes = [];
  let nextStrikeAt = 0;
  let lastMs = null;
  let stepAcc = 0;
  let coronaAcc = 0;

  let camOffsetX = 0;
  let camOffsetY = 0;

  const v3 = new THREE.Vector3();
  const dir3 = new THREE.Vector3();

  /* ------------------------------------------------------------ projection */

  function screenToWorldAtZ(px, py, z, out) {
    const ndcX = (px / width) * 2 - 1;
    const ndcY = -((py / height) * 2 - 1);
    v3.set(ndcX, ndcY, 0.5).unproject(camera);
    dir3.copy(v3).sub(camera.position).normalize();
    // The camera looks down -z, so dir3.z is negative and never zero here.
    const t = (z - camera.position.z) / dir3.z;
    out.copy(camera.position).addScaledVector(dir3, t);
    return out;
  }

  const wordCentre = new THREE.Vector3();
  const wordHalf = new THREE.Vector3();
  const scratch = new THREE.Vector3();
  const target = new THREE.Vector3();

  function readWord(anchor) {
    screenToWorldAtZ(anchor.x, anchor.y, ORIGIN_Z, wordCentre);
    screenToWorldAtZ(anchor.right, anchor.bottom, ORIGIN_Z, scratch);
    wordHalf.set(
      Math.abs(scratch.x - wordCentre.x) + CRAWL_PAD,
      Math.abs(scratch.y - wordCentre.y) + CRAWL_PAD,
      0
    );
  }

  /* --------------------------------------------------------------- strikes */

  function pushSegment(x1, y1, z1, x2, y2, z2, now, gen, crawl, isFill, peak) {
    if (segCount >= MAX_SEGMENTS) return;
    const o = segCount * SEG;
    segs[o] = x1;
    segs[o + 1] = y1;
    segs[o + 2] = z1;
    segs[o + 3] = x2;
    segs[o + 4] = y2;
    segs[o + 5] = z2;
    segs[o + 6] = now;
    segs[o + 7] = crawl ? 1 : 0;
    segs[o + 8] = isFill ? 1 : 0;
    // Deeper generations are dimmer, so a strike reads as a trunk with
    // filaments rather than as a bush of equal lines.
    segs[o + 9] = peak * Math.pow(GEN_FALLOFF, gen);
    segCount++;
  }

  /* Lightning descends, and it comes from the sky — off the top of the page,
     not out of the headline. Launching on the wordmark's top edge put the
     origin about eighty units above the glyphs, so a bolt appeared to start
     just over the type instead of arriving from above the viewport.

     The x spread is wider than the wordmark so a strike is not always aimed
     straight at it; the crawl engages later, when a channel actually reaches
     the glyph box. */
  const skyPoint = new THREE.Vector3();

  function perimeterPoint(out) {
    // A little above the top edge of the screen, so the channel is already
    // running by the time it enters the frame.
    screenToWorldAtZ(width * 0.5, -SKY_MARGIN, ORIGIN_Z, skyPoint);
    out.x = (rand() - 0.5) * 2 * wordHalf.x * SKY_SPREAD;
    out.y = skyPoint.y - wordCentre.y;
    return out;
  }

  const off = { x: 0, y: 0 };

  function spawnStreamer(now, lag, replaySlowest, crawl, life, peak) {
    perimeterPoint(off);
    tips.push({
      x: wordCentre.x + off.x,
      y: wordCentre.y + off.y,
      z: ORIGIN_Z,
      // Straight down, tilted. DOWN is -PI/2 in world terms, and the spread is
      // bounded so a channel can lean but can never set off sideways.
      a: -Math.PI / 2 + (rand() - 0.5) * 2 * LAUNCH_TILT,
      // Elevation carries the bolt away from the camera: a strike that stays
      // on the wordmark's plane is the 2D field with extra steps.
      e: -0.2 - rand() * 0.6,
      life,
      gen: 0,
      crawl: 0,
      // Armed, not engaged: the channel has to reach the glyph box first.
      crawlPending: crawl > 0,
      fill: replaySlowest,
      peak,
    });
  }

  function spawnStrike(now, backlog, replaySlowest) {
    // Reach scales with the backlog: an unresolved span throws the discharge
    // deeper into the volume, which is the field's only backlog channel now
    // that nothing is drawn between strikes.
    const lag = clamp(backlog / 26, 0, 1);
    target.set(
      // Below and to one side, never level: the target sets the bolt's overall
      // slope, and a target at the same height produces the sideways strike.
      wordCentre.x + (rand() - 0.5) * lerp(220, 520, lag) * tune.reach,
      wordCentre.y - lerp(320, 780, lag) * tune.reach,
      lerp(ORIGIN_Z - 140, DEPTH_FAR * tune.reach, lag)
    );

    const hi = Math.max(1, Math.round(tune.streamers));
    const lo = Math.min(STREAMERS_MIN, hi);
    const n = lo + Math.floor(rand() * (hi - lo + 1));
    for (let i = 0; i < n; i++) {
      // One streamer carries the crawl; the rest leave the electrode directly,
      // so the letterforms light once rather than n times over.
      spawnStreamer(
        now,
        lag,
        replaySlowest,
        i === 0 ? CRAWL_STEPS : 0,
        Math.round(lerp(60, 130, lag) * (0.55 + rand() * 0.65)),
        tune.peak
      );
    }

    // Restrike. Real lightning hits the same channel two or three times inside
    // a couple of hundred milliseconds, and that flicker is most of what makes
    // it read as lightning rather than as a drawn line.
    const strikes = Math.floor(rand() * (RESTRIKE_MAX + 1));
    for (let i = 1; i <= strikes; i++) {
      restrikes.push({
        at: now + i * (RESTRIKE_GAP_MS + rand() * RESTRIKE_VAR_MS),
        lag,
        fill: replaySlowest,
      });
    }
  }

  function stepTips(now) {
    const next = [];
    for (const t of tips) {
      if (t.life <= 0 || segCount >= MAX_SEGMENTS) continue;

      const x0 = t.x;
      const y0 = t.y;
      const z0 = t.z;

      /* Engage the crawl on arrival: once a falling channel reaches the top of
         the glyph box and is within its width, it starts hugging the
         letterforms. Granted at spawn instead, the crawl ran in mid-air above
         the wordmark. */
      if (t.crawlPending && t.y <= wordCentre.y + wordHalf.y) {
        if (Math.abs(t.x - wordCentre.x) <= wordHalf.x) {
          t.crawl = CRAWL_STEPS;
        }
        t.crawlPending = false;
      }

      if (t.crawl > 0) {
        /* Crawl phase: hug the glyph box before breaking away, so the charge
           reads as accumulating on the letters rather than as a bolt that
           happens to start near them.

           It DESCENDS. The previous version walked the perimeter freely, and
           the box's top and bottom edges are horizontal — so a crawl launched
           on the top edge produced a dead-level run up to fourteen steps long
           before the bolt ever left the wordmark. That was the horizontal
           lightning, not the cone clamp, which was working. */
        const drop = STEP * 0.62;
        t.y -= drop;
        t.x += (rand() - 0.5) * drop * 0.8;
        // Stay within the glyph box horizontally; leave it downward only.
        const dx = clamp(t.x - wordCentre.x, -wordHalf.x, wordHalf.x);
        t.x = wordCentre.x + dx;
        // Heading is kept pointing down so the break-away continues the descent
        // instead of setting off from whatever the crawl last wandered to.
        t.a = -Math.PI / 2 + (rand() - 0.5) * 0.5;

        t.crawl--;
        if (t.y < wordCentre.y - wordHalf.y) t.crawl = 0;
        pushSegment(x0, y0, z0, t.x, t.y, t.z, now, t.gen, true, t.fill, t.peak);
        t.life--;
        next.push(t);
        continue;
      }

      t.a += (rand() - 0.5) * JITTER;
      t.e += (rand() - 0.5) * JITTER * 0.6;

      // A hard break every few steps. Smooth wander reads as a drawn curve;
      // the sharp angular kink is the single strongest lightning cue there is.
      if (rand() < tune.kink) {
        t.a += (rand() < 0.5 ? -1 : 1) * KINK;
        t.e += (rand() - 0.5) * KINK * 0.6;
      }

      const tx = target.x - t.x;
      const ty = target.y - t.y;
      const tz = target.z - t.z;
      const want = Math.atan2(ty, tx);
      let d = want - t.a;
      while (d > Math.PI) d -= Math.PI * 2;
      while (d < -Math.PI) d += Math.PI * 2;
      t.a += d * PULL;
      t.e += (Math.atan2(tz, Math.hypot(tx, ty)) - t.e) * PULL;

      /* Hold the channel inside the descending cone. Jitter, kinks and the
         pull toward the target can each individually turn a tip past
         horizontal, and once past it the bolt climbs back up the page. */
      /* Elevation is clamped as well as azimuth. Unbounded, it drifts past
         a right angle, the tip starts travelling almost purely along Z, and a
         run of those segments projects from the vanishing point as a near
         horizontal line however tightly the azimuth is held. cos(MAX_PITCH)
         stays above 0.6, so lateral motion always dominates depth motion and
         the descending cone actually governs what appears on screen. */
      t.e = clamp(t.e, -MAX_PITCH, MAX_PITCH * 0.35);

      let da = t.a + Math.PI / 2;
      while (da > Math.PI) da -= Math.PI * 2;
      while (da < -Math.PI) da += Math.PI * 2;
      t.a = -Math.PI / 2 + clamp(da, -MAX_TILT, MAX_TILT);

      const ce = Math.cos(t.e);
      t.x += Math.cos(t.a) * STEP * ce;
      t.y += Math.sin(t.a) * STEP * ce;
      t.z += Math.sin(t.e) * STEP;

      pushSegment(x0, y0, z0, t.x, t.y, t.z, now, t.gen, false, t.fill, t.peak);
      t.life--;

      const forkP = tune.branch * Math.pow(BRANCH_GEN_DECAY, t.gen);
      if (t.gen < MAX_GEN && next.length < MAX_TIPS && rand() < forkP) {
        next.push({
          x: t.x,
          y: t.y,
          z: t.z,
          a: t.a + (rand() - 0.5) * 2 * BRANCH_SPREAD, // re-clamped next step
          e: t.e + (rand() - 0.5) * 0.5,
          life: Math.round(t.life * GEN_FALLOFF),
          gen: t.gen + 1,
          crawl: 0,
          crawlPending: false,
          fill: t.fill,
          peak: t.peak,
        });
      }
      if (t.life > 0) next.push(t);
    }
    tips = next;
  }

  /* Compact the segment pool in place, dropping anything past the cull floor.
     Segments are born in order, so the survivors stay ordered and the arcs do
     not reshuffle their draw order between frames. */
  function cull(now) {
    let w = 0;
    for (let i = 0; i < segCount; i++) {
      const o = i * SEG;
      const age = now - segs[o + 6];
      const hold = segs[o + 7] ? CRAWL_HOLD_MS : tune.hold;
      if (age < RISE_MS + hold + QUENCH_MS + TAIL_MS) {
        if (w !== i) segs.copyWithin(w * SEG, o, o + SEG);
        w++;
      }
    }
    segCount = w;
  }

  function ageAlpha(age, crawl, peak) {
    const hold = crawl ? CRAWL_HOLD_MS : tune.hold;
    if (age < RISE_MS) return (age / RISE_MS) * peak;
    if (age < RISE_MS + hold) return peak;
    const q = age - RISE_MS - hold;
    if (q < QUENCH_MS) return lerp(peak, peak * AFTERGLOW, q / QUENCH_MS);
    return peak * AFTERGLOW * Math.exp(-(q - QUENCH_MS) / TAIL_MS);
  }

  /* ---------------------------------------------------------------- render */

  function render(nowMs, env) {
    const t = nowMs / 1000;
    const reduced = Boolean(env.reduced);
    const intensity = clamp(env.intensity, 0.15, 1.4);

    if (!reduced) {
      const cx = Math.sin(t * 0.13) * 40 + camOffsetX;
      const cy = Math.cos(t * 0.097) * 26 + camOffsetY;
      camera.position.set(cx, cy, CAM_Z + Math.sin(t * 0.08) * 22);
      camera.lookAt(Math.sin(t * 0.06) * 16, Math.cos(t * 0.07) * 10, 0);
    }
    camera.updateMatrixWorld();
    readWord(env.anchor);

    const dt = lastMs === null ? 0 : Math.min((nowMs - lastMs) / 1000, 0.05);
    lastMs = nowMs;

    const backlog = log.backlog;
    const readers = log.readers;
    let slowest = readers[0];
    for (const r of readers) if (r.pos < slowest.pos) slowest = r;
    const replaySlowest = slowest.kind === "replay";

    if (!reduced) {
      if (nextStrikeAt === 0) nextStrikeAt = nowMs + 400;

      // A strike is an append event, not a timer that happens to fire.
      const appends = log.drainAppends();
      if (nowMs >= nextStrikeAt && appends > 0) {
        spawnStrike(nowMs, backlog, replaySlowest);
        /* The dial is the average gap in seconds, jittered +/-35%. Expressed
           as a rate multiplier it was impossible to aim: the reviewer could not
           reach a lightning cadence because he was tuning a ratio against a
           hidden base interval instead of setting the interval. */
        const gap = tune.gap * 1000 * (0.65 + rand() * 0.7);
        // Denser punctuation when work is unresolved, floored so it never
        // becomes a hum.
        // Backlog still tightens the cadence, but only by a third — enough to
        // read as pressure, not enough to turn the field into a hum.
        nextStrikeAt = nowMs + Math.max(1200, gap * lerp(1, 0.68, clamp(backlog / 26, 0, 1)));
      }

      // Restrikes queued by the last strike — the flicker of a multi-stroke bolt.
      for (let i = restrikes.length - 1; i >= 0; i--) {
        const q = restrikes[i];
        if (nowMs < q.at) continue;
        const n = 1 + Math.floor(rand() * 3);
        for (let k = 0; k < n; k++) {
          spawnStreamer(nowMs, q.lag, q.fill, 0, Math.round(lerp(40, 95, q.lag)), tune.peak);
        }
        restrikes.splice(i, 1);
      }

      /* Corona. Short streamers leaving the letterforms continuously, so the
         electrode is never inert between strikes. Faint and always dying —
         this is the Tesla-coil idle, not a second discharge. */
      coronaAcc += dt * tune.corona * intensity;
      while (coronaAcc >= 1) {
        coronaAcc -= 1;
        spawnStreamer(nowMs, 0, false, 0, CORONA_LIFE, A_CORONA);
      }

      stepAcc += dt * STEPS_PER_SEC;
      let steps = Math.floor(stepAcc);
      stepAcc -= steps;
      if (steps > 6) steps = 6; // a returning tab must not grow a whole bolt at once
      while (steps-- > 0 && tips.length) stepTips(nowMs);
    }

    cull(nowMs);

    /* --- write the buffers ---------------------------------------------- */

    const zSpan = ORIGIN_Z - DEPTH_FAR;
    let drawn = 0;
    for (let i = 0; i < segCount; i++) {
      const o = i * SEG;
      const age = nowMs - segs[o + 6];
      let a = ageAlpha(age, segs[o + 7] === 1, segs[o + 9]);
      if (a < CULL) continue;

      const z1 = segs[o + 2];
      const z2 = segs[o + 5];
      const depthT = clamp(((z1 + z2) * 0.5 - DEPTH_FAR) / zSpan, 0, 1);
      a = Math.min(segs[o + 9], a * lerp(DEPTH_DIM, 1, depthT));

      const p = drawn * 6;
      positions[p] = segs[o];
      positions[p + 1] = segs[o + 1];
      positions[p + 2] = z1;
      positions[p + 3] = segs[o + 3];
      positions[p + 4] = segs[o + 4];
      positions[p + 5] = z2;

      const c = segs[o + 8] === 1 ? fill : accent;

      const q = drawn * 8;
      colorArray[q] = c.r;
      colorArray[q + 1] = c.g;
      colorArray[q + 2] = c.b;
      colorArray[q + 3] = a;
      colorArray[q + 4] = c.r;
      colorArray[q + 5] = c.g;
      colorArray[q + 6] = c.b;
      colorArray[q + 7] = a;

      drawn++;

      /* Fringe. Live bolt only — a halo on the fading afterimage smears the
         whole field. The threshold is against the segment's own peak, so a dim
         branch never glows because the trunk beside it does. */
      if (tune.glow > 0 && a >= segs[o + 9] * GLOW_FLOOR && drawn + 2 <= MAX_DRAW) {
        gA.set(segs[o], segs[o + 1], z1);
        gB.set(segs[o + 3], segs[o + 4], z2);
        gDir.subVectors(gB, gA);
        if (gDir.lengthSq() > 1e-6) {
          gDir.normalize();
          // Perpendicular to the segment AND to the view ray, which is a
          // screen-space perpendicular whatever the segment's orientation.
          gToCam.copy(camera.position).sub(gA).normalize();
          gPerp.crossVectors(gDir, gToCam);
          if (gPerp.lengthSq() > 1e-6) {
            gPerp.normalize();
            const dist = camera.position.distanceTo(gA);
            const off = GLOW_SPREAD * (dist / CAM_Z);
            const fa = a * tune.glow * 0.42;
            for (let sgn = -1; sgn <= 1; sgn += 2) {
              const fp = drawn * 6;
              const fq = drawn * 8;
              positions[fp] = gA.x + gPerp.x * off * sgn;
              positions[fp + 1] = gA.y + gPerp.y * off * sgn;
              positions[fp + 2] = gA.z + gPerp.z * off * sgn;
              positions[fp + 3] = gB.x + gPerp.x * off * sgn;
              positions[fp + 4] = gB.y + gPerp.y * off * sgn;
              positions[fp + 5] = gB.z + gPerp.z * off * sgn;
              colorArray[fq] = c.r;
              colorArray[fq + 1] = c.g;
              colorArray[fq + 2] = c.b;
              colorArray[fq + 3] = fa;
              colorArray[fq + 4] = c.r;
              colorArray[fq + 5] = c.g;
              colorArray[fq + 6] = c.b;
              colorArray[fq + 7] = fa;
              drawn++;
            }
          }
        }
      }
    }

    geometry.attributes.position.needsUpdate = true;
    geometry.attributes.color.needsUpdate = true;
    geometry.setDrawRange(0, drawn * 2);

    renderer.render(scene, camera);
  }

  /* Reduced motion: one frozen strike mid-flight, grown once and never aged. */
  function buildStatic(env) {
    segCount = 0;
    tips = [];
    readWord(env.anchor);
    spawnStrike(0, 14, false);
    for (let i = 0; i < 120 && tips.length; i++) stepTips(0);
    // Stagger the birth times backwards so the still carries the same tip-to-
    // root falloff a moving strike does.
    const msPerStep = 1000 / STEPS_PER_SEC;
    for (let i = 0; i < segCount; i++) {
      segs[i * SEG + 6] = -(segCount - i) * msPerStep;
    }
    tips = [];
  }

  let staticKey = "";

  return {
    render(nowMs, env) {
      if (env.reduced) {
        const key = `${Math.round(env.anchor.x / 4)}|${Math.round(env.anchor.y / 4)}|${width}`;
        if (key !== staticKey) {
          camera.updateMatrixWorld();
          buildStatic(env);
          staticKey = key;
        }
        render(0, env);
        return;
      }
      staticKey = "";
      render(nowMs, env);
    },

    setParam(key, value) {
      // Every knob is read per frame, so nothing here needs a rebuild.
      if (key in tune) tune[key] = value;
    },

    resize(w, h, nextDpr) {
      width = w;
      height = h;
      dpr = nextDpr;
      renderer.setPixelRatio(dpr);
      renderer.setSize(width, height, false);
      camera.aspect = width / height;
      camera.updateProjectionMatrix();
    },

    pointer(x, y) {
      if (x === null || y === null) {
        camOffsetX = 0;
        camOffsetY = 0;
        return;
      }
      // Camera parallax only. No geometry responds to the pointer.
      camOffsetX = (x / width - 0.5) * 30;
      camOffsetY = -(y / height - 0.5) * 20;
    },

    destroy() {
      scene.remove(lines);
      geometry.dispose();
      material.dispose();
      renderer.dispose();
      renderer.forceContextLoss();
    },
  };
}
