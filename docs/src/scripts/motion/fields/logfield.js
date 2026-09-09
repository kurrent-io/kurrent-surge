/* "The Log Becomes The Field" — the hero's live log, promoted to page substrate.

   Records stream horizontally across the whole viewport on several parallel
   lanes. Every lane is the same log at a different offset and a slightly
   different zoom, the way a log looks when you widen the window on it. Scroll
   position scrubs the window through the log while the model keeps appending,
   so the field flows on its own and answers the reader at the same time.

   Every mark is a record: index entries stand taller, the backlog past the
   slowest reader stays bright until it is consumed, and the three reader
   cursors sit below their lane's baseline with the replaying one on the fill
   stop. No rules, no glow, no second hue. */

import { clamp, crisp, lerp, mulberry32 } from "../field-kit.js";

export const meta = {
  id: "logfield",
  title: "The Log Becomes The Field",
  engine: "2d",
};

/* ------------------------------------------------------------------ layout */

const SPACING = 26; // px between records on a lane of scale 1

/* One lane per this many px of viewport height at intensity 1: 900px lands on
   7 lanes, which is dense enough to read as a field and sparse enough that no
   paragraph sits between two lanes at full strength. */
const LANE_PITCH = 128;
const LANE_MIN = 4;
const LANE_MAX = 10;

const LANE_TOP = 0.06; // fraction of height kept clear at each end
const LANE_SPAN = 0.88;
const LANE_JITTER = 18; // px; a perfectly even ruler reads as a grid, not a log

const H_TICK = 5.5;
const H_INDEX = 8.5;
const H_HEAD = 11.5;
const H_READER = 5;
const READER_DROP = 12; // below the lane baseline

/* ------------------------------------------------------------------ alphas */

const A_CONSUMED = 0.1;
const A_PENDING = 0.24;
const A_INDEX_LIFT = 0.07;
const A_HEAD = 0.42;
const A_READER = 0.38;
const A_RECENCY = 0.3;

/* The intensity dial scales every raw alpha; A_HEAD stays the hard ceiling, so
   the dial changes what is reached and never what is permitted. Scaling the
   ladder top instead only coarsens the buckets, which makes a higher dial read
   dimmer than a lower one. */
const GAIN_MIN = 0.55;
const GAIN_MAX = 1.15;

/* Below this a record is doing nothing but costing a line segment. */
const A_CULL = 0.018;

/* Per-record alpha varies with lane, pointer distance and append recency, so
   stroking each on its own would cost one call per record. Alphas quantise into
   this many buckets and each bucket strokes once, which holds a frame at a
   dozen calls for the whole field however many lanes are on screen. */
const BUCKETS = 7;

/* The ladder is spaced by a power, not evenly. Nearly every record sits in the
   bottom third of the 0..0.42 range, and an even ladder flattens that third
   into one step — which erases the vertical alpha profile that keeps the field
   off the body copy. */
const LADDER_GAMMA = 1.45;
const INV_LADDER_GAMMA = 1 / LADDER_GAMMA;
const LADDER = Array.from({ length: BUCKETS }, (_, b) =>
  A_HEAD * Math.pow(b / (BUCKETS - 1), LADDER_GAMMA)
);

/* Mid-viewport is where 17px body copy lives, so lanes there run at a third of
   the strength of lanes at the top and bottom of the frame. Without this the
   field reads as a stripe pattern laid over the text. */
const MID_DIM = 0.42;
const PROFILE_POWER = 1.15;
const BOTTOM_DIM = 0.78;

/* ------------------------------------------------------------------- scrub */

/* Roughly one record per this many px of scroll: fast enough to read as
   scrubbing, slow enough that a trackpad flick is not a jump. */
const SCROLL_PX_PER_RECORD = 62;

/* The log has no future, so the scrub can only run backward into history. tanh
   bounds it at this fraction of the viewport width: a long page must not walk
   the append head off the right edge and leave the field with nothing but
   consumed records. Gain is ~1 for the first screenful, then eases off. */
const SCRUB_LIMIT_FRAC = 0.34;

/* Raw scrollY steps by whole wheel notches; this smooths them into a scrub.
   Short enough that the field still tracks the finger. */
const SCRUB_TAU = 0.14; // seconds

/* -------------------------------------------------------------------- edge */

const EDGE_FADE_L = 0.14; // fraction of width
const EDGE_FADE_R = 0.1;

const POINTER_GAIN = 0.16;
const POINTER_FALLOFF_X = 92; // px
const POINTER_FALLOFF_Y = 150; // px; wide enough to reach the neighbouring lanes
const POINTER_LIFT = 2.6; // px added to a tick's half-height under the cursor

/* A record is only fresh for the few appends after it lands; skipping the
   recency call past that is most of the per-record cost of the loop. */
const RECENCY_WINDOW = 6; // records

export function create(setup) {
  const ctx = setup.ctx;
  const colors = setup.colors;
  const log = setup.log;

  let width = setup.width;
  let height = setup.height;

  let lanes = [];
  let laneCount = 0;

  let pointerX = null;
  let pointerY = null;

  let scrub = 0; // records, damped
  let lastMs = null;
  let primed = false;

  /* Strongest at the top and bottom of the frame, quietest through the middle,
     and never quite symmetric so the field does not read as a mirror. */
  function laneProfile(v) {
    const edge = Math.pow(Math.abs(v * 2 - 1), PROFILE_POWER);
    return lerp(MID_DIM, 1, edge) * lerp(1, BOTTOM_DIM, v);
  }

  function laneCountFor(intensity) {
    const base = height / LANE_PITCH;
    const scaled = base * lerp(0.62, 1.22, clamp(intensity, 0, 1.4) / 1.4);
    return clamp(Math.round(scaled), LANE_MIN, LANE_MAX);
  }

  /* Fixed seed, and lane values are drawn in lane order, so a resize that keeps
     the lane count keeps every lane exactly where it was. */
  function buildLanes(count) {
    const rand = mulberry32(0x10617);
    const top = height * LANE_TOP;
    const span = height * LANE_SPAN;
    const next = [];

    for (let i = 0; i < count; i++) {
      const slot = count === 1 ? 0.5 : (i + 0.5) / count;
      const y = clamp(
        top + span * slot + (rand() - 0.5) * LANE_JITTER,
        4,
        height - 4
      );
      const scale = 0.84 + rand() * 0.3;
      next.push({
        y,
        scale,
        spacing: SPACING * scale,
        // The append head sits at a different x on every lane, so the heads and
        // the index ticks never stack into a vertical column.
        headX: width * (0.6 + rand() * 0.3),
        weight: laneProfile(y / height) * (0.8 + rand() * 0.25),
      });
    }

    lanes = next;
    laneCount = count;
  }

  function ensureLanes(intensity) {
    const wanted = laneCountFor(intensity);
    if (wanted !== laneCount || lanes.length === 0) buildLanes(wanted);
  }

  function render(nowMs, env) {
    const dt = lastMs === null ? 0 : clamp((nowMs - lastMs) / 1000, 0, 0.05);
    lastMs = nowMs;

    const intensity = clamp(env.intensity ?? 1, 0, 1.4);
    ensureLanes(intensity);

    const gain = lerp(GAIN_MIN, GAIN_MAX, intensity / 1.4);

    // Scroll is direct manipulation, so it still applies under reduced motion —
    // but reduced motion means no RAF, so the damping would never converge.
    const limit = (width * SCRUB_LIMIT_FRAC) / SPACING;
    const target =
      limit * Math.tanh(env.scrollY / SCROLL_PX_PER_RECORD / limit);
    if (env.reduced || !primed) scrub = target;
    else scrub += (target - scrub) * (1 - Math.exp(-dt / SCRUB_TAU));
    primed = true;

    const head = log.head;
    const lastRecord = Math.floor(head);
    const slowest = log.slowest;
    const readers = log.readers;

    const fadeL = width * EDGE_FADE_L;
    const fadeR = width * EDGE_FADE_R;
    const invFx2 = 1 / (POINTER_FALLOFF_X * POINTER_FALLOFF_X);
    const invFy2 = 1 / (POINTER_FALLOFF_Y * POINTER_FALLOFF_Y);

    ctx.clearRect(0, 0, width, height);
    ctx.lineWidth = 1;
    ctx.lineCap = "butt";

    const accent = new Array(BUCKETS);
    const fill = new Array(BUCKETS);

    /* One shared alpha ladder across every lane, so the whole field costs at
       most 2 * BUCKETS strokes. Bucket 0 would stroke at alpha 0, so anything
       still worth drawing is lifted to bucket 1 and anything below A_CULL is
       dropped instead. */
    const put = (paths, alpha, x, y0, y1) => {
      if (alpha < A_CULL) return;
      const b = Math.max(
        1,
        Math.min(
          BUCKETS - 1,
          Math.round(
            Math.pow(alpha / A_HEAD, INV_LADDER_GAMMA) * (BUCKETS - 1)
          )
        )
      );
      let path = paths[b];
      if (!path) path = paths[b] = new Path2D();
      path.moveTo(crisp(x), crisp(y0));
      path.lineTo(crisp(x), crisp(y1));
    };

    for (let i = 0; i < lanes.length; i++) {
      const lane = lanes[i];
      const spacing = lane.spacing;
      const baseY = lane.y;
      const weight = lane.weight * gain;

      // Screen x of record `head` on this lane, after the scrub.
      const originX = lane.headX + scrub * spacing;

      const laneTick = H_TICK * lane.scale;
      const laneIndex = H_INDEX * lane.scale;

      let pointerLane = 0;
      if (pointerY !== null) {
        const dy = baseY - pointerY;
        pointerLane = Math.exp(-dy * dy * invFy2);
        if (pointerLane < 0.02) pointerLane = 0;
      }

      const from = Math.ceil(head - (originX + spacing) / spacing);
      const to = Math.min(
        lastRecord,
        Math.floor(head + (width + spacing - originX) / spacing)
      );

      for (let n = from; n <= to; n++) {
        const x = originX - (head - n) * spacing;

        const isIndex = log.isIndex(n);
        // Everything the slowest reader has not reached is still work, so the
        // lag reads as the width of a bright run rather than as a marker.
        let alpha = n > slowest ? A_PENDING : A_CONSUMED;
        if (isIndex) alpha += A_INDEX_LIFT;

        const age = head - n;
        if (age < RECENCY_WINDOW) alpha += A_RECENCY * log.recency(n);

        let half = isIndex ? laneIndex : laneTick;

        if (pointerLane > 0) {
          const dx = x - pointerX;
          const g = pointerLane * Math.exp(-dx * dx * invFx2);
          alpha += POINTER_GAIN * g;
          half += POINTER_LIFT * g * lane.scale;
        }

        alpha *= weight;

        /* The field runs to both edges, where a hard cut would read as a
           cropped rule. Records shrink as well as dim on the way out, so the
           ramp survives the alpha quantisation. */
        let edge = 1;
        if (x < fadeL) edge = clamp(x / fadeL, 0, 1);
        else if (x > width - fadeR) edge = clamp((width - x) / fadeR, 0, 1);
        if (edge < 1) {
          alpha *= edge;
          half *= 0.55 + 0.45 * edge;
        }

        put(accent, alpha, x, baseY - half, baseY + half);
      }

      // --- the append head ------------------------------------------------
      if (originX >= 0 && originX <= width) {
        const headHalf = H_HEAD * lane.scale;
        put(
          accent,
          A_HEAD * weight,
          originX,
          baseY - headHalf,
          baseY + headHalf
        );
      }

      // --- reader cursors ---------------------------------------------------
      // Below the baseline so they read as pointers into the log rather than as
      // records. The replaying one takes the fill stop: it is the one element
      // doing a different kind of work.
      const drop = READER_DROP * lane.scale;
      const readerHalf = H_READER * lane.scale;
      for (let r = 0; r < readers.length; r++) {
        const rx = originX - (head - readers[r].pos) * spacing;
        if (rx < 0 || rx > width) continue;
        const paths = readers[r].kind === "replay" ? fill : accent;
        put(
          paths,
          A_READER * weight,
          rx,
          baseY + drop - readerHalf,
          baseY + drop + readerHalf
        );
      }
    }

    ctx.strokeStyle = colors.accent;
    for (let b = 1; b < BUCKETS; b++) {
      if (!accent[b]) continue;
      ctx.globalAlpha = LADDER[b];
      ctx.stroke(accent[b]);
    }

    ctx.strokeStyle = colors.fill;
    for (let b = 1; b < BUCKETS; b++) {
      if (!fill[b]) continue;
      ctx.globalAlpha = LADDER[b];
      ctx.stroke(fill[b]);
    }

    ctx.globalAlpha = 1;
  }

  return {
    render,
    resize(w, h) {
      width = w;
      height = h;
      // Geometry only. Whether the new height wants a different lane count is
      // ensureLanes' call, and the host renders a frame straight after this.
      if (laneCount > 0) buildLanes(laneCount);
    },
    pointer(x, y) {
      pointerX = x;
      pointerY = y;
    },
    destroy() {
      lanes = [];
      laneCount = 0;
    },
  };
}
