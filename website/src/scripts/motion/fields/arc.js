/* Arc Discharge — the wordmark as an electrode.

   Charge collects on the letters of "Surge" and leaves them. A strike is born
   on the wordmark's perimeter, crawls along the glyph box for a run, then
   breaks away and walks down-right across the viewport hunting for a reader to
   ground in. The log runs out of the name: the append head sits beside the
   wordmark's right edge and records stream away from it, so reaching a reader
   is that reader consuming the record — the arc terminates and the ground point
   flashes. Reaching none, it fizzles.

   The log is never drawn. Records have no marks on screen; the model decides
   only when and where things happen. The backlog — everything between the
   slowest reader and the head — is carried entirely by how the arcs behave
   inside it: charge there decays four times slower than charge outside it, and
   the span crackles on its own account in proportion to its width. A growing
   lag is a widening region that will not settle, not a row of ticks.

   A strike is loud by area and by persistence, not by breaching the alpha
   ceiling: the whole bolt sits at full strength for a third of a second, then
   quenches to a dim afterimage that visibly winks out. Between strikes the
   field is nearly empty — four stationary marks and the odd crackle — and that
   emptiness is what makes the next strike land. */

import { crisp, clamp, lerp, mulberry32 } from "../field-kit.js";

export const meta = { id: "arc", title: "Arc Discharge", engine: "2d" };

/* ---------------------------------------------------------------- geometry */

/* The log leaves the wordmark heading down and right, into the empty half of
   the hero. Measured in the browser at 1440x900: the axis runs between y=45 and
   y=243 and clears the sub-headline by 130px at the closest point. */
const AXIS_ANGLE = 0.16; // rad
const HEAD_GAP = 16; // px clear of the glyph box, so the head is beside it
const HEAD_DROP = 0.2; // fraction of the anchor height below its centre
const GROUND_DROP = 12; // readers sit off the log line, as in the retired hero log strip
const GROUND_R = 15; // px; a tip this close to a ground point snaps to it

/* Records-per-pixel is derived, not fixed: the wordmark eats the left of the
   viewport, and on a narrow screen a fixed 26px pitch would push every reader
   off the right edge and leave the arcs nothing to ground in. 26 is the retired strip's
   pitch and the ceiling; the run only tightens when it has to. Nothing draws a
   record — this is the scale that places the readers and sizes the backlog. */
const SPACING_MAX = 26;
const SPACING_MIN = 10;
const RUN_RECORDS = 34; // the backlog is 30; the rest is margin

const TWO_PI = Math.PI * 2;

/* ------------------------------------------------------------------ growth */

const STEP = 14; // px per growth step
/* A strike must cross ~800px in a quarter second: any slower and it reads as a
   drawn line rather than a discharge, any faster and it is gone inside two
   frames. 190 steps/s is ≈2.7 kpx/s of tip travel. */
const STEPS_PER_SEC = 190;

const TRUNK_JITTER = 0.3; // rad of angular noise per step
const TRUNK_PULL = 0.05; // fraction of the heading error corrected per step
const BRANCH_P = 0.13; // per step, per tip
const BRANCH_KEEP = 0.55; // fraction of the parent's remaining reach
const MAX_GEN = 3;

/* ------------------------------------------------------------------- crawl */

/* The surface run. Charge that appears at a point and leaps immediately reads
   as a bolt that happens to start near the word; charge that travels along the
   letterforms first reads as the word doing it. */
const CRAWL_PAD = 7; // px outside the glyph box — never over the type
const CRAWL_MIN = 6;
const CRAWL_SPAN = 13; // 6..18 steps at 14px is 84..252px of a ~820px perimeter
const CRAWL_WOBBLE = 3.2; // px of outward noise, so the run is not a drawn rect
const CRAWL_BRANCH_P = 0.22; // filaments jumping off the letters mid-crawl
/* A discharge leaves a surface along its normal, then bends into the potential
   gradient at once. Without this the trunk breaks off the underside heading
   straight down and burns its whole budget turning back toward the log: the
   grounding rate fell by three quarters when this was left at 0. */
const BREAK_BIAS = 0.55;

/* A bad seed can branch geometrically. The cap is the hard stop: growth simply
   halts, which costs a truncated arc rather than a dropped frame. */
const MAX_SEGMENTS = 560;
const MAX_ARCS = 18;

/* ------------------------------------------------------------------- decay */

/* Four phases, and the hold is the one that makes a strike an event. A bolt
   whose segments start decaying the instant they are drawn is only ever bright
   at the tip: the eye sees a moving dot, not a discharge. Holding the whole
   run at full strength for a third of a second puts the entire branching
   structure on screen at once, which is what "unmistakable" costs.

   The crawl holds more than twice as long as the leap, so once the bolt has
   quenched the letterforms are still lit. That run is what ties the field to
   the product name, so it is the last thing to go dark. */
const ATTACK_MS = 30; // ≈6 growth steps; the tip is at strength almost at once
const HOLD_LEAP = 300;
const HOLD_CRAWL = 850;
/* Then a fast drop to a dim plateau. Without the quench the strike fades
   smoothly into its own afterimage and the decay reads as more of the same. */
const QUENCH_MS = 150;
/* Fraction of peak the plateau sits at. Raised once the record ticks were
   removed: the afterimage is where the backlog's decay time becomes visible,
   and at 0.16 the plateau carried too little of the frame for a 4x difference
   in how long it lasted to register as anything. */
const AFTERGLOW = 0.22;
/* Persistence is the only channel the backlog has left now that no record is
   drawn, so the two are held far apart: charge over un-consumed records lingers
   more than four times longer than charge past the slowest reader. A strike
   that lands inside a wide backlog leaves a visible afterimage for over a
   second; the same strike outside one is gone almost at once. */
const TAU_QUIET = 300; // plateau decay outside the backlog
const TAU_BACKLOG = 1400; // and inside it, where the work is unresolved
/* Above the bottom bucket, not below it: a tail that lingers at the quantiser
   floor never ends, and the wink-out is half of what makes decay legible. */
const VISIBLE_FLOOR = 0.035;

/* ------------------------------------------------------------------ alphas */

/* The ceiling belongs to the live strike and to the marks that flash when a
   record is consumed. The four standing marks — three grounds and the head —
   are the entire permanent structure of the field, so they sit high: well above
   the perceptual floor for a 1px stroke on #0f181f, which is where this field
   previously lived and why it read as nothing at all. */
const A_TIP = 0.45;
const A_CRACKLE = 0.34;
const A_GROUND = 0.34;
const A_GROUND_FLASH = 0.45;
const A_HEAD = 0.4;
const A_HEAD_FLASH = 0.45;
/* Branches carry most of the trunk's strength. Dimmer than this and a strike
   reads as one line with debris rather than as a discharge with structure. */
const GEN_FALLOFF = [1, 0.82, 0.6, 0.44];

/* ----------------------------------------------------------------- pointer */

const POINTER_R = 260; // px
const POINTER_LEAN = 0.35; // fraction of the way toward the cursor, branch only

/* ------------------------------------------------------------------ timing */

const STRIKE_MIN_MS = 2000;
const STRIKE_SPAN_MS = 2000;
const CRACKLE_P = 0.55; // share of appends that crackle
const CRACKLE_ON_WORD = 0.45; // and this share of those crackle on the letters
const HEAD_FLASH_MS = 220;
const GROUND_FLASH_MS = 460;

/* Per-segment alpha varies continuously with age, so stroking each on its own
   would cost one call per segment. Alphas quantise into buckets and each
   bucket strokes once, which holds a full strike at a dozen calls. */
const A_BUCKETS = 7;

const SEG = 7; // stride: x1, y1, x2, y2, bornMs, generation, holdMs

const bucketOf = (a, max, n) => clamp(Math.floor((a / max) * n), 0, n - 1);
/* Top of the bucket, so the brightest bucket renders at exactly `max`. A
   mid-bucket or floor mapping quietly costs the strike its peak — the ceiling
   is 0.45 and the field was rendering its brightest element at 0.41. */
const bucketAlpha = (b, max, n) => (max * (b + 1)) / n;

export function create(setup) {
  const ctx = setup.ctx;
  const colors = setup.colors;
  const log = setup.log;

  let width = setup.width;
  let height = setup.height;

  let pointerX = null;
  let pointerY = null;
  let lastMs = null;

  /* One stream for the whole session. Reseeding on resize would reshuffle
     arcs that are already on screen. */
  const rand = mulberry32(0x9e3779b9);

  let arcs = [];
  let liveSegments = 0;
  let staticArcs = null;
  let staticKey = "";

  let nextStrikeAt = 0;
  let headFlashAt = -1e9;
  const groundFlash = [-1e9, -1e9, -1e9];

  /* Rebuilt every frame from env.anchor and the log model, and read by the
     growth walk. The wordmark moves with the scroll, so none of this is fixed. */
  const word = { left: 0, right: 0, top: 0, bottom: 0, w: 0, h: 0, perim: 0 };
  const headPt = { x: 0, y: 0 };
  const axis = { dx: 1, dy: 0, px: 0, py: 1 }; // heading, and its perpendicular
  let spacing = SPACING_MAX;
  let grounds = [];

  /* ---------------------------------------------------------- the geometry */

  function readGeometry(anchor) {
    // Inflated: the crawl hugs the outside of the glyph box. Anything inside it
    // sits under the copy and is simply not seen.
    word.left = anchor.left - CRAWL_PAD;
    word.right = anchor.right + CRAWL_PAD;
    word.top = anchor.top - CRAWL_PAD;
    word.bottom = anchor.bottom + CRAWL_PAD;
    word.w = Math.max(1, word.right - word.left);
    word.h = Math.max(1, word.bottom - word.top);
    word.perim = 2 * (word.w + word.h);

    axis.dx = Math.cos(AXIS_ANGLE);
    axis.dy = Math.sin(AXIS_ANGLE);
    axis.px = -axis.dy;
    axis.py = axis.dx;

    headPt.x = anchor.right + HEAD_GAP;
    headPt.y = anchor.y + (anchor.bottom - anchor.top) * HEAD_DROP;

    const run = Math.max(140, width - headPt.x - 24);
    spacing = clamp(run / RUN_RECORDS, SPACING_MIN, SPACING_MAX);

    const head = log.head;
    const readers = log.readers;
    grounds = new Array(readers.length);
    for (let i = 0; i < readers.length; i++) {
      // Distance behind the head, in pixels along the log axis. A lagging
      // reader is visibly further out; this is the mapping records use.
      const d = (head - readers[i].pos) * spacing;
      grounds[i] = {
        x: headPt.x + axis.dx * d + axis.px * GROUND_DROP,
        y: headPt.y + axis.dy * d + axis.py * GROUND_DROP,
        d,
        index: i,
        replay: readers[i].kind === "replay",
      };
    }
  }

  /** Ground points a tip could plausibly reach; the rest are off the canvas. */
  function reachableGrounds() {
    const out = [];
    for (const g of grounds) {
      if (g.x > -40 && g.x < width + 40 && g.y > -40 && g.y < height + 40) out.push(g);
    }
    return out;
  }

  function groundNear(x, y) {
    for (const g of grounds) {
      const dx = g.x - x;
      const dy = g.y - y;
      if (dx * dx + dy * dy < GROUND_R * GROUND_R) return g;
    }
    return null;
  }

  const insideWord = (x, y) =>
    x > word.left && x < word.right && y > word.top && y < word.bottom;

  /* A point at arc-length `s` around the inflated glyph box, with the outward
     normal there. Top edge first, then right, bottom, left. */
  function onPerimeter(s, out) {
    let t = s % word.perim;
    if (t < 0) t += word.perim;

    if (t < word.w) {
      out.x = word.left + t;
      out.y = word.top;
      out.nx = 0;
      out.ny = -1;
    } else if ((t -= word.w) < word.h) {
      out.x = word.right;
      out.y = word.top + t;
      out.nx = 1;
      out.ny = 0;
    } else if ((t -= word.h) < word.w) {
      out.x = word.right - t;
      out.y = word.bottom;
      out.nx = 0;
      out.ny = 1;
    } else {
      out.x = word.left;
      out.y = word.bottom - (t - word.w);
      out.nx = -1;
      out.ny = 0;
    }
    return out;
  }

  /* Where charge leaves the letters. Weighted to the right edge and the
     underside, because that is the half of the box the log runs into; a strike
     launched off the top has to curve back around the type to reach ground. */
  function launchS(r) {
    const u = r();
    if (u < 0.42) return word.w + r() * word.h; // right
    if (u < 0.84) return word.w + word.h + r() * word.w; // bottom
    if (u < 0.94) return r() * word.w; // top
    return 2 * word.w + word.h + r() * word.h; // left
  }

  /* --------------------------------------------------------------- arc life */

  const scratch = { x: 0, y: 0, nx: 0, ny: 0 };

  function makeArc(now, tip, opts) {
    return {
      segs: [],
      tips: [tip],
      peak: opts.peak,
      jitter: opts.jitter,
      pull: opts.pull,
      branchP: opts.branchP,
      target: opts.target ?? null,
      /* The caught-up reader sits barely one record out from the head, so every
         arc leaving the wordmark passes through it. Grounding stays disarmed
         until the tip has spent most of its run: a discharge with the energy to
         cross the backlog does not stop at the first pin it brushes. */
      arm: opts.arm,
      steps: 0,
      fill: false,
      stepAcc: 0,
      retireMs: now,
    };
  }

  function makeTip(x, y, dir, gen, budget, crawl, s, sDir) {
    return { x, y, dir, gen, budget, crawl, s, sDir };
  }

  function pushSegment(arc, x1, y1, x2, y2, now, gen, hold) {
    arc.segs.push(x1, y1, x2, y2, now, gen, hold);
    // Retire once the newest segment has quenched past the visible floor;
    // holding it any longer only starves the segment budget.
    arc.retireMs = now + ATTACK_MS + hold + TAU_BACKLOG * 2.4;
    liveSegments += 1;
  }

  function dropArc(i) {
    liveSegments -= arcs[i].segs.length / SEG;
    arcs.splice(i, 1);
  }

  /** Free room for a new strike by retiring the oldest afterimages first. */
  function makeRoom(want) {
    while (arcs.length && (liveSegments + want > MAX_SEGMENTS || arcs.length >= MAX_ARCS)) {
      dropArc(0);
    }
  }

  function branchDir(x, y, dir, r) {
    let off = 0.45 + r() * 0.6;
    if (r() < 0.5) off = -off;
    if (pointerX !== null) {
      const dx = pointerX - x;
      const dy = pointerY - y;
      if (dx * dx + dy * dy < POINTER_R * POINTER_R) {
        // Lean the branch toward the cursor rather than aiming it: attraction
        // that resolves fully reads as a magnet, not as a field.
        let delta = Math.atan2(dy, dx) - (dir + off);
        while (delta > Math.PI) delta -= TWO_PI;
        while (delta < -Math.PI) delta += TWO_PI;
        off += delta * POINTER_LEAN;
      }
    }
    return dir + off;
  }

  function stepArc(arc, now, r, ground) {
    const tips = arc.tips;
    arc.steps += 1;
    const armed = ground && arc.steps > arc.arm;

    for (let i = tips.length - 1; i >= 0; i--) {
      const tip = tips[i];
      if (tip.budget <= 0 || liveSegments >= MAX_SEGMENTS) {
        tips.splice(i, 1);
        continue;
      }

      /* --- the surface run ------------------------------------------------ */
      if (tip.crawl > 0) {
        tip.s += tip.sDir * STEP;
        onPerimeter(tip.s, scratch);
        const wob = r() * CRAWL_WOBBLE;
        const nx = scratch.x + scratch.nx * wob;
        const ny = scratch.y + scratch.ny * wob;
        pushSegment(arc, tip.x, tip.y, nx, ny, now, tip.gen, HOLD_CRAWL);
        tip.x = nx;
        tip.y = ny;
        tip.crawl -= 1;

        // A filament that jumps off the letters mid-run, rather than the whole
        // discharge waiting politely for the end of the crawl.
        if (tip.gen < MAX_GEN && liveSegments < MAX_SEGMENTS && r() < CRAWL_BRANCH_P) {
          tips.push(
            makeTip(
              nx,
              ny,
              Math.atan2(scratch.ny, scratch.nx) + (r() - 0.5) * 1.1,
              tip.gen + 1,
              5 + Math.floor(r() * 6),
              0,
              0,
              1
            )
          );
        }
        // Break away along the outward normal, so the trunk leaves the glyphs
        // rather than cutting back across them, then bend toward the log.
        if (tip.crawl === 0) {
          let dir = Math.atan2(scratch.ny, scratch.nx) + (r() - 0.5) * 0.9;
          if (arc.target) {
            let delta = Math.atan2(arc.target.y - ny, arc.target.x - nx) - dir;
            while (delta > Math.PI) delta -= TWO_PI;
            while (delta < -Math.PI) delta += TWO_PI;
            dir += delta * BREAK_BIAS;
          }
          tip.dir = dir;
        }
        continue;
      }

      /* --- the leap ------------------------------------------------------- */
      if (arc.target) {
        // The trunk homes on its ground point; branches barely do, so they
        // curl away and die, which is what makes the strike look grown.
        let delta =
          Math.atan2(arc.target.y - tip.y, arc.target.x - tip.x) - tip.dir;
        while (delta > Math.PI) delta -= TWO_PI;
        while (delta < -Math.PI) delta += TWO_PI;
        tip.dir += delta * (tip.gen === 0 ? arc.pull : arc.pull * 0.25);
      }
      tip.dir += (r() - 0.5) * arc.jitter;

      const nx = tip.x + Math.cos(tip.dir) * STEP;
      const ny = tip.y + Math.sin(tip.dir) * STEP;

      // Growth back under the type is invisible and would spend the whole
      // budget behind the copy. The tip dies at the boundary instead.
      if (insideWord(nx, ny)) {
        tips.splice(i, 1);
        continue;
      }

      pushSegment(arc, tip.x, tip.y, nx, ny, now, tip.gen, HOLD_LEAP);
      tip.x = nx;
      tip.y = ny;
      tip.budget -= 1;

      if (nx < -30 || nx > width + 30 || ny < -40 || ny > height + 40) {
        tips.splice(i, 1);
        continue;
      }

      if (armed) {
        const g = groundNear(nx, ny);
        if (g) {
          pushSegment(arc, nx, ny, g.x, g.y, now, tip.gen, HOLD_LEAP);
          groundFlash[g.index] = now;
          // The replay reader is the one doing a different kind of work, so an
          // arc that discharges into it takes the fill stop.
          arc.fill = g.replay;
          tips.length = 0;
          return;
        }
      }

      if (tip.gen < MAX_GEN && r() < arc.branchP) {
        tips.push(
          makeTip(
            nx,
            ny,
            branchDir(nx, ny, tip.dir, r),
            tip.gen + 1,
            Math.floor(tip.budget * BRANCH_KEEP),
            0,
            0,
            1
          )
        );
      }
    }
  }

  /* ---------------------------------------------------------------- spawns */

  /** The far edge of the backlog most of the time, so strikes cross its width. */
  function pickTarget(r) {
    const candidates = reachableGrounds();
    if (!candidates.length) return null;
    candidates.sort((a, b) => b.d - a.d);
    if (candidates.length === 1 || r() < 0.55) return candidates[0];
    return candidates[1 + Math.floor(r() * (candidates.length - 1))];
  }

  function spawnStrike(now, intensity, r) {
    const target = pickTarget(r);
    if (!target) return;

    const s = launchS(r);
    onPerimeter(s, scratch);
    const x0 = scratch.x + scratch.nx * 1.5;
    const y0 = scratch.y + scratch.ny * 1.5;

    const reach = Math.hypot(target.x - x0, target.y - y0) / STEP;
    /* 1.6× the straight-line distance: a wandering trunk needs slack or it
       fizzles a step short of ground every time, and the break-away costs a few
       steps of its own before the heading settles. */
    const budget = clamp(
      Math.round(reach * 1.6 * (0.75 + 0.35 * intensity)),
      26,
      130
    );
    const crawl = CRAWL_MIN + Math.floor(r() * CRAWL_SPAN);

    makeRoom(budget + crawl);
    arcs.push(
      makeArc(
        now,
        makeTip(x0, y0, 0, 0, budget, crawl, s, r() < 0.5 ? -1 : 1),
        {
          peak: A_TIP,
          jitter: TRUNK_JITTER,
          pull: TRUNK_PULL,
          branchP: BRANCH_P * clamp(intensity, 0.2, 1.4),
          target,
          /* Counted from the launch point, and always shorter than the run to
             the target, so an arc can still discharge where it was aimed. The
             crawl is added because those steps cover no ground. */
          arm: crawl + clamp(Math.floor(reach * 0.75), 1, 12),
        }
      )
    );
  }

  /** A small local discharge that goes nowhere. This is the resting texture. */
  function spawnCrackle(now, x, y, intensity, r) {
    if (liveSegments > MAX_SEGMENTS * 0.85 || arcs.length >= MAX_ARCS) return;
    arcs.push(
      makeArc(
        now,
        makeTip(x, y + (r() - 0.5) * 8, r() * TWO_PI, 0, 4 + Math.floor(r() * 4), 0, 0, 1),
        {
          peak: A_CRACKLE,
          jitter: 0.9,
          pull: 0,
          branchP: 0.06 * clamp(intensity, 0.2, 1.4),
          target: null,
          arm: Infinity, // a crackle has nowhere to go; it always fizzles
        }
      )
    );
  }

  /** Charge sitting on the letters between strikes. Crawls, never leaps. */
  function spawnSurfaceCrackle(now, intensity, r) {
    if (liveSegments > MAX_SEGMENTS * 0.85 || arcs.length >= MAX_ARCS) return;
    const s = launchS(r);
    onPerimeter(s, scratch);
    arcs.push(
      makeArc(
        now,
        makeTip(scratch.x, scratch.y, 0, 0, 3, 4 + Math.floor(r() * 5), s, r() < 0.5 ? -1 : 1),
        {
          peak: A_CRACKLE,
          jitter: 0.9,
          pull: 0,
          branchP: 0.05 * clamp(intensity, 0.2, 1.4),
          target: null,
          arm: Infinity,
        }
      )
    );
  }

  /* --------------------------------------------------------------- drawing */

  function segAlpha(age, hold, tau, gen, peak) {
    if (age < 0) return 0;
    const strength = peak * GEN_FALLOFF[gen];
    if (age < ATTACK_MS) return strength * (age / ATTACK_MS);
    const t = age - ATTACK_MS - hold;
    if (t <= 0) return strength;
    const quench = AFTERGLOW + (1 - AFTERGLOW) * Math.exp(-t / QUENCH_MS);
    return strength * quench * Math.exp(-t / tau);
  }

  function drawScene(now, list) {
    const backlog = log.backlog;
    const dSlowest = (log.head - log.slowest) * spacing;

    // Records still owed to a reader hold their charge longer. With no ticks on
    // screen this is the backlog's only persistent channel, so it does the
    // whole job: a wider span is a region that takes visibly longer to settle.
    const tauIn = lerp(TAU_QUIET, TAU_BACKLOG, clamp(backlog / 24, 0, 1));
    const tauOut = TAU_QUIET * 0.5;

    ctx.clearRect(0, 0, width, height);
    ctx.lineWidth = 1;
    ctx.lineCap = "butt";
    ctx.strokeStyle = colors.accent;

    /* --- arcs ------------------------------------------------------------- */
    const accentPaths = new Array(A_BUCKETS);
    const fillPaths = new Array(A_BUCKETS);

    for (const arc of list) {
      const segs = arc.segs;
      const paths = arc.fill ? fillPaths : accentPaths;
      for (let i = 0; i < segs.length; i += SEG) {
        // Projected onto the log axis: inside the backlog span means between
        // the head and the slowest reader, whatever the arc's own wandering.
        const d =
          (segs[i] - headPt.x) * axis.dx + (segs[i + 1] - headPt.y) * axis.dy;
        const tau = d > -24 && d < dSlowest + 24 ? tauIn : tauOut;
        const a = segAlpha(
          now - segs[i + 4],
          segs[i + 6],
          tau,
          segs[i + 5],
          arc.peak
        );
        if (a < VISIBLE_FLOOR) continue;
        const b = bucketOf(a, A_TIP, A_BUCKETS);
        let path = paths[b];
        if (!path) path = paths[b] = new Path2D();
        path.moveTo(crisp(segs[i]), crisp(segs[i + 1]));
        path.lineTo(crisp(segs[i + 2]), crisp(segs[i + 3]));
      }
    }

    for (let b = 0; b < A_BUCKETS; b++) {
      if (!accentPaths[b]) continue;
      ctx.globalAlpha = bucketAlpha(b, A_TIP, A_BUCKETS);
      ctx.stroke(accentPaths[b]);
    }
    ctx.strokeStyle = colors.fill;
    for (let b = 0; b < A_BUCKETS; b++) {
      if (!fillPaths[b]) continue;
      ctx.globalAlpha = bucketAlpha(b, A_TIP, A_BUCKETS);
      ctx.stroke(fillPaths[b]);
    }

    /* Nothing stands on screen between strikes. The ground points and the head
       are still computed — an arc has to know where to terminate, and the
       backlog is still read off them — but drawing them put four short strokes
       on an otherwise empty field that read as stray marks sliding through the
       middle of the discharge. The replay ground was the worst of them: it
       travels 780px along the axis over ~14s. The field is now arcs only. */

    ctx.globalAlpha = 1;
  }

  /* ----------------------------------------------------------------- static */

  /* One frozen strike, mid-flight: launched off the wordmark from a fixed seed
     and stopped short of ground. Deterministic, so the same wordmark position
     always redraws the same frame. */
  function buildStatic() {
    const r = mulberry32(0x51d4a2);
    const saveArcs = arcs;
    const saveLive = liveSegments;
    arcs = [];
    liveSegments = 0;

    const target =
      reachableGrounds().sort((a, b) => b.d - a.d)[0] ??
      {
        x: headPt.x + axis.dx * 700,
        y: headPt.y + axis.dy * 700,
      };
    // The lower-right corner of the box: the launch the weighting favours, and
    // the one that reads clearly in a still.
    const s = word.w + word.h * 0.72;
    onPerimeter(s, scratch);
    const reach = Math.hypot(target.x - scratch.x, target.y - scratch.y) / STEP;
    const arc = makeArc(
      0,
      makeTip(scratch.x, scratch.y, 0, 0, Math.round(reach * 1.1), 9, s, 1),
      {
        peak: A_TIP,
        jitter: TRUNK_JITTER,
        pull: TRUNK_PULL,
        branchP: BRANCH_P,
        target,
        // Grounding disabled: a still cannot show a discharge resolving, so the
        // arc is caught hanging mid-strike instead.
        arm: Infinity,
      }
    );

    let steps = 0;
    while (arc.tips.length && steps < 160) {
      stepArc(arc, 0, r, false);
      steps += 1;
    }
    /* Age the run backwards from the tip at the rate it would really have grown
       at. That spans ~845ms over 160 steps, so the still carries exactly the
       falloff a moving strike does: the newest third at full strength, the rest
       quenching, the crawl on the letters still lit behind it. Stretching the
       stagger to force a steeper gradient only culls two thirds of the arc
       below the visible floor and leaves a stub. */
    const msPerStep = 1000 / STEPS_PER_SEC;
    const count = arc.segs.length / SEG;
    for (let i = 0; i < count; i++) {
      arc.segs[i * SEG + 4] = -(count - i) * msPerStep;
    }

    staticArcs = [arc];
    arcs = saveArcs;
    liveSegments = saveLive;
  }

  /* ------------------------------------------------------------------ frame */

  function render(nowMs, env) {
    readGeometry(env.anchor);

    if (env.reduced) {
      // The wordmark travels with the scroll, and the host re-renders on scroll
      // even when the loop is stopped. Quantised to 4px: a still frame must not
      // rebuild itself for sub-pixel drift.
      const key = `${Math.round(word.left / 4)}|${Math.round(word.top / 4)}|${width}`;
      if (!staticArcs || key !== staticKey) {
        buildStatic();
        staticKey = key;
      }
      drawScene(0, staticArcs);
      return;
    }
    staticArcs = null;

    const dt = lastMs === null ? 0 : Math.min((nowMs - lastMs) / 1000, 0.05);
    lastMs = nowMs;

    const intensity = clamp(env.intensity, 0, 1.4);
    // The dial reaches zero; a zero divisor would park the strike timer at
    // Infinity and the field would go permanently dark.
    const rate = clamp(intensity, 0.15, 1.4);

    /* --- absorb the log --------------------------------------------------- */
    const appends = log.drainAppends();
    if (appends > 0) headFlashAt = nowMs;

    const backlog = log.backlog;
    /* The backlog's second channel: density. A wide span of un-consumed records
       crackles along its whole length, a narrow one barely at all, so the lag
       is legible as a region of activity rather than as a measured bar. Ranged
       hard — 0.08 at a backlog of 6 to 0.85 at 30 — because with no ticks drawn
       this and the decay time are the only two ways the span can be seen. */
    const backlogP = clamp((backlog - 4) / 26, 0, 0.85) * rate;

    for (let i = 0; i < appends; i++) {
      if (nowMs >= nextStrikeAt) {
        spawnStrike(nowMs, intensity, rand);
        /* The backlog's third channel: cadence. Unresolved work makes the field
           discharge more often — 1.7-3.3s at full lag against 3.1-6.2s at none.
           The lag cycles over about fourteen seconds, so the field breathes
           with it. Kept above 1.7s at the fastest: denser punctuation is still
           punctuation, a constant hum is not. */
        const lagGain = 0.65 + 0.55 * clamp(backlog / 26, 0, 1);
        nextStrikeAt =
          nowMs + (STRIKE_MIN_MS + rand() * STRIKE_SPAN_MS) / (rate * lagGain);
      } else if (rand() < CRACKLE_P) {
        if (rand() < CRACKLE_ON_WORD) spawnSurfaceCrackle(nowMs, intensity, rand);
        else spawnCrackle(nowMs, headPt.x, headPt.y, intensity, rand);
      }
      if (rand() < backlogP) {
        /* Clamped to the canvas. On a narrow viewport the span runs off the
           right edge, and an un-clamped spawn puts most of these crackles
           off-screen where they are killed on their first step — which throws
           away the density channel exactly where it is needed most. */
        const span = Math.min(backlog * spacing, width - headPt.x - 20);
        const d = rand() * Math.max(0, span);
        spawnCrackle(
          nowMs,
          headPt.x + axis.dx * d,
          headPt.y + axis.dy * d,
          intensity,
          rand
        );
      }
    }
    if (nextStrikeAt === 0) nextStrikeAt = nowMs + STRIKE_MIN_MS;

    /* --- grow and retire -------------------------------------------------- */
    for (const arc of arcs) {
      arc.stepAcc += dt * STEPS_PER_SEC;
      while (arc.stepAcc >= 1 && arc.tips.length) {
        arc.stepAcc -= 1;
        stepArc(arc, nowMs, rand, true);
      }
    }
    for (let i = arcs.length - 1; i >= 0; i--) {
      if (!arcs[i].tips.length && nowMs > arcs[i].retireMs) dropArc(i);
    }

    drawScene(nowMs, arcs);
  }

  return {
    render,
    resize(w, h) {
      width = w;
      height = h;
      // Arc geometry is absolute; after a reflow it describes the old canvas
      // and the old wordmark position.
      arcs = [];
      liveSegments = 0;
      staticArcs = null;
    },
    pointer(x, y) {
      pointerX = x;
      pointerY = y;
    },
    destroy() {
      arcs = [];
      staticArcs = null;
      liveSegments = 0;
    },
  };
}
