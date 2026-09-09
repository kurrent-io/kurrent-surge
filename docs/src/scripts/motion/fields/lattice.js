/* Charged lattice.

   A jittered lattice of vertices over the whole viewport, edges between near
   neighbours, and charge that conducts along those edges. Every append injects
   a packet at the lattice column under the append head; it travels leftward,
   branches where an edge happens to be a good conductor, and dies where it is
   not. The three readers are drains: charge that arrives at one is absorbed
   there, so a lagging reader stops the run further left. The stretch between
   the slowest reader and the head sits at an elevated rest charge, so the
   backlog reads as the width of a charged run rather than as a bar.

   The cursor is a probe touched to the grid. It injects charge at the contact
   point and nothing else: the lattice's shape answers only to its own drift.
   Pulling vertices toward a moving point is the most recognisable water cue
   there is, and no amount of electrical behaviour on top of it survives the
   surface rippling underneath.

   WebGL, because a frame is roughly 1350 edges expanded to 8100 vertices and
   Canvas 2D cannot stroke that at 60fps. */

import { clamp, compileProgram, makeNoise2D, mulberry32 } from "../field-kit.js";

export const meta = { id: "lattice", title: "Charged Lattice", engine: "webgl" };

/* Record geometry, copied from the retired hero log strip so the two fields place a record at
   the same x for the same log position. */
const RECORD_SPACING = 26; // px between records
const HEAD_X = 0.82; // fraction of width where new records land

/* ------------------------------------------------------------------ lattice */

const SPACING_BASE = 78; // CSS px at intensity 0
const SPACING_SPAN = 16; // ... falling to 62 at intensity 1
/* A 4K viewport at hero spacing is four thousand vertices. Spacing opens up
   past this count instead, so the frame cost is bounded by the dial and not by
   the size of the monitor. */
const MAX_VERTICES = 1100;
const NEIGHBOUR_R = 1.45; // × spacing; orthogonals always, most diagonals
const JITTER = 0.3; // × spacing

const DRIFT_AMP = 0.17; // × spacing
const DRIFT_SPEED = 0.085; // noise units per second
const DRIFT_SCALE = 0.55; // noise units per lattice step

/* -------------------------------------------------------------------- charge */

const DECAY = 0.55; // 1/s; a run dies after roughly 600px of travel
/* Fraction of a vertex's charge that leaves it per second, shared out among
   its downstream edges. Two things about that are load-bearing. The budget is
   per vertex, not per edge, or a packet divides among five or six neighbours
   and falls under IGNITE within three hops. And charge is pushed downstream
   rather than diffused down a gradient, because a gradient stops at
   equalisation, which halves the packet at every hop and reads as a smudge
   that spreads two cells and stops. This is a current: it empties one vertex
   into the next. */
const OUT_RATE = 9;
/* A vertex below this stops conducting, so a branch that runs out of charge
   dies where it stands instead of fading into a uniform glow. */
const IGNITE = 0.05;
/* Share of an edge's weight that survives pointing across the lattice instead
   of down it. Near zero and the current runs in straight horizontal lines. */
const DIR_MIN = 0.12;
const COND_MIN = 0.18;
const COND_SPAN = 1.15;
/* Squaring the draw leaves most edges poor conductors and a few excellent
   ones. Retune this first if the effect reads wrong. */
const COND_BIAS = 2;
/* And the share is that weight to the fourth power, which is what keeps the
   current a filament rather than a wash: the best edge out of a vertex takes
   most of the budget, and a branch only happens where two edges are close. */
const PATH_EXP = 4;
const CHARGE_MAX = 2.2;
const INJECT = 1.6;
const INJECT_SIDE = 0.5;

const STRAND_CUT = 0.9; // reduced motion: fraction of vertices left dark

const DRAIN_R = 0.55; // × spacing
/* The live reader is caught up, so it sits barely a record behind the head and
   its band overlaps the injection column. It has to be a weak drain or every
   append is absorbed at birth; the two readers with a backlog to work through
   are the ones that stop a run. */
const ABSORB_LIVE = 0.9; // 1/s
const ABSORB_LAG = 3;
const ABSORB_REPLAY = 3.6;

/* --------------------------------------------------------------------- ink */

/* The substrate has to be a structure that is present, not a suggestion: a
   page-level opacity ramp over something nearly invisible is still nearly
   invisible. This is roughly a UI border's weight in the accent hue. */
const A_REST = 0.13;
const A_ACCENT_MAX = 0.45;
const A_FILL_MAX = 0.45;
/* Charge at which an edge is half bright. Low, because the brightness that has
   to carry is a run a few hops out from its injection at a third of its packet,
   not the injection crest itself. */
const KNEE = 0.2;
/* Rest levels the log features hold when no charge is passing, as a fraction
   of full brightness. The ladder: ground 0.130, backlog band 0.181, a dying
   branch 0.194, live and lagging seams 0.239, replay seam 0.280, a cursor
   contact 0.317, a travelling run 0.359, an append crest 0.415, ceiling 0.45.
   Crest over ground is 3.2× and a run over the band it crosses is 2.0×, which
   is the contrast that actually matters: most runs travel inside the backlog
   band, so a band held any higher eats the current that crosses it. */
const F_BACKLOG = 0.16;
const F_READER = 0.34;
const F_REPLAY = 0.47;

/* The cursor contacts the grid once per this much travel, so the contact rate
   follows the distance swept rather than the frame rate, and a flick lays down
   a run of contacts instead of one dot at the final position. Well under half
   a cell: sampled any coarser, a fast diagonal sweep steps over whole vertices
   and the trail comes out dotted. Several samples landing on the same vertex is
   the intended behaviour, not waste — that is what makes the contact bright
   where the cursor actually crossed one. */
const PROBE_STEP = 0.45; // × spacing
const PROBE_CHARGE = 0.2; // one sample, well under an append's packet
/* A pointer that jumps — a re-entry, a scrolled page — would otherwise stripe
   the whole viewport in one frame. Past this, only the tail of the sweep lands. */
const PROBE_MAX = 14;

/* gl.lineWidth() is clamped to 1.0 device pixel on every implementation that
   matters, which at dpr 2 is a half-CSS-pixel hairline that resolves to grey
   mush. Edges are triangle quads instead, and because the whole pipeline works
   in CSS pixels this half-width is exactly 1 CSS pixel at any dpr. */
const HALF_W = 0.5;

const VERTEX_SRC = `
attribute vec2 aPos;
attribute float aAlpha;
attribute float aTint;
uniform vec2 uResolution;
varying float vAlpha;
varying float vTint;
void main() {
  vec2 clip = (aPos / uResolution) * 2.0 - 1.0;
  gl_Position = vec4(clip.x, -clip.y, 0.0, 1.0);
  vAlpha = aAlpha;
  vTint = aTint;
}`;

const FRAGMENT_SRC = `
precision mediump float;
uniform vec3 uAccent;
uniform vec3 uFill;
varying float vAlpha;
varying float vTint;
void main() {
  vec3 tone = mix(uAccent, uFill, vTint);
  // The context is premultipliedAlpha, and the blend is source-over rather
  // than additive: overlapping edges must not accumulate into a halo.
  gl_FragColor = vec4(tone * vAlpha, vAlpha);
}`;

/** Stable across a resize: keyed on grid coordinates, not on a running index. */
const cellRandom = (col, row, salt) =>
  mulberry32((col * 73856093) ^ (row * 19349663) ^ (salt * 83492791));

export function create(setup) {
  const gl = setup.gl;
  const canvas = setup.canvas;
  const log = setup.log;
  const colors = setup.colors;

  let width = setup.width;
  let height = setup.height;

  const noise = makeNoise2D(0x1a771ce);

  const program = compileProgram(gl, VERTEX_SRC, FRAGMENT_SRC);
  const locPos = gl.getAttribLocation(program, "aPos");
  const locAlpha = gl.getAttribLocation(program, "aAlpha");
  const locTint = gl.getAttribLocation(program, "aTint");
  const uResolution = gl.getUniformLocation(program, "uResolution");
  const uAccent = gl.getUniformLocation(program, "uAccent");
  const uFill = gl.getUniformLocation(program, "uFill");
  const buffer = gl.createBuffer();

  let spacing = 62;
  let builtStep = -1; // quantised intensity the current lattice was built for

  let cols = 0;
  let rows = 0;
  let count = 0;
  let vx0 = new Float32Array(0);
  let vy0 = new Float32Array(0);
  let vseed = new Float32Array(0);
  let px = new Float32Array(0);
  let py = new Float32Array(0);
  let charge = new Float32Array(0);
  let delta = new Float32Array(0);
  let valpha = new Float32Array(0);
  let vtint = new Float32Array(0);

  let edgeCount = 0;
  let ea = new Int32Array(0); // upstream end, the one nearer the head
  let eb = new Int32Array(0); // downstream end
  let ew = new Float32Array(0); // share of the upstream vertex's outflow budget
  let data = new Float32Array(0);

  let headCol = new Int32Array(0);
  let injectCursor = 0;
  let injectStride = 1;

  let pointerX = null;
  let pointerY = null;
  let probeX = null; // where the cursor last discharged
  let probeY = null;
  let lastMs = null;

  /* ---------------------------------------------------------------- build */

  function build(intensity) {
    const dial = Number.isFinite(intensity) ? clamp(intensity, 0, 1.4) : 1;
    const area = Math.max(1, width * height);
    spacing = Math.max(
      SPACING_BASE - SPACING_SPAN * dial,
      Math.sqrt(area / MAX_VERTICES)
    );

    // One cell of overshoot on every side, so edges reach past the viewport
    // and the lattice has no visible border.
    cols = Math.ceil(width / spacing) + 3;
    rows = Math.ceil(height / spacing) + 3;
    count = cols * rows;

    vx0 = new Float32Array(count);
    vy0 = new Float32Array(count);
    vseed = new Float32Array(count);
    px = new Float32Array(count);
    py = new Float32Array(count);
    charge = new Float32Array(count);
    delta = new Float32Array(count);
    valpha = new Float32Array(count);
    vtint = new Float32Array(count);

    const amp = JITTER * spacing;
    for (let row = 0; row < rows; row++) {
      for (let col = 0; col < cols; col++) {
        const i = row * cols + col;
        const rand = cellRandom(col, row, 1);
        vx0[i] = (col - 1) * spacing + (rand() - 0.5) * 2 * amp;
        vy0[i] = (row - 1) * spacing + (rand() - 0.5) * 2 * amp;
        vseed[i] = rand();
      }
    }

    // Half the neighbourhood only: the other half is the same edge seen from
    // the far end. Jitter pushes some pairs past the radius, and the holes it
    // leaves are what stop the lattice from looking woven.
    const radius = NEIGHBOUR_R * spacing;
    const radius2 = radius * radius;
    const cap = count * 4;
    const ta = new Int32Array(cap);
    const tb = new Int32Array(cap);
    const tc = new Float32Array(cap);
    const steps = [
      [1, 0],
      [0, 1],
      [1, 1],
      [1, -1],
    ];

    let n = 0;
    for (let row = 0; row < rows; row++) {
      for (let col = 0; col < cols; col++) {
        const i = row * cols + col;
        for (let s = 0; s < steps.length; s++) {
          const nc = col + steps[s][0];
          const nr = row + steps[s][1];
          if (nc < 0 || nc >= cols || nr < 0 || nr >= rows) continue;
          const j = nr * cols + nc;
          const dx = vx0[j] - vx0[i];
          const dy = vy0[j] - vy0[i];
          const d2 = dx * dx + dy * dy;
          if (d2 > radius2 || d2 < 1e-6) continue;

          // Charge only ever travels down the log, so an edge has a fixed
          // upstream end: the one nearer the head.
          const r = cellRandom(col, row, 2 + s)();
          const cond = COND_MIN + COND_SPAN * Math.pow(r, COND_BIAS);
          const lw = Math.abs(dx) / Math.sqrt(d2);
          const p = cond * (DIR_MIN + (1 - DIR_MIN) * lw);
          ta[n] = dx > 0 ? j : i;
          tb[n] = dx > 0 ? i : j;
          tc[n] = Math.pow(p, PATH_EXP);
          n++;
        }
      }
    }

    edgeCount = n;
    ea = ta.slice(0, n);
    eb = tb.slice(0, n);
    ew = tc.slice(0, n);

    // Normalise each vertex's outgoing weights once, so conduction is a single
    // multiply per edge per frame.
    const wsum = new Float32Array(count);
    for (let e = 0; e < n; e++) wsum[ea[e]] += ew[e];
    for (let e = 0; e < n; e++) {
      const total = wsum[ea[e]];
      ew[e] = total > 0 ? ew[e] / total : 0;
    }

    // Two triangles, four floats a vertex. Sized once here and refilled with
    // bufferSubData every frame; nothing on the render path allocates.
    data = new Float32Array(n * 24);
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.bufferData(gl.ARRAY_BUFFER, data.byteLength, gl.DYNAMIC_DRAW);

    const headX = width * HEAD_X;
    const column = [];
    for (let i = 0; i < count; i++) {
      if (Math.abs(vx0[i] - headX) <= spacing * 0.55) column.push(i);
    }
    column.sort((a, b) => vy0[a] - vy0[b]);
    headCol = Int32Array.from(column);
    /* A low-discrepancy walk down the column: consecutive appends enter far
       apart, where a plain increment would read as a scan line. The stride has
       to be coprime with the column height or the walk closes into a short
       cycle and part of the column never receives an append at all. */
    const len = headCol.length;
    const gcd = (a, b) => (b === 0 ? a : gcd(b, a % b));
    injectStride = Math.max(1, Math.round(len * 0.381));
    while (injectStride > 1 && gcd(len, injectStride) !== 1) injectStride--;
    injectCursor = 0;

    builtStep = Math.round(dial * 10);
  }

  /* -------------------------------------------------------------- physics */

  const xOfPos = (pos, headXpx, head) =>
    headXpx - (head - pos) * RECORD_SPACING;

  function inject() {
    const pending = log.drainAppends();
    if (!pending || headCol.length === 0) return;
    for (let k = 0; k < pending; k++) {
      injectCursor = (injectCursor + injectStride) % headCol.length;
      charge[headCol[injectCursor]] += INJECT;
      if (injectCursor > 0) charge[headCol[injectCursor - 1]] += INJECT_SIDE;
      if (injectCursor < headCol.length - 1) {
        charge[headCol[injectCursor + 1]] += INJECT_SIDE;
      }
    }
  }

  /** Charge into the vertex nearest a point. The grid is anchored, so the
      cell is arithmetic and only its 3×3 neighbourhood has to be searched:
      jitter plus drift can move a vertex just under one cell from its slot. */
  function contact(x, y, amount) {
    const col = Math.round(x / spacing) + 1;
    const row = Math.round(y / spacing) + 1;
    let best = -1;
    let bestD = Infinity;
    for (let r = row - 1; r <= row + 1; r++) {
      if (r < 0 || r >= rows) continue;
      for (let c = col - 1; c <= col + 1; c++) {
        if (c < 0 || c >= cols) continue;
        const i = r * cols + c;
        const dx = px[i] - x;
        const dy = py[i] - y;
        const d = dx * dx + dy * dy;
        if (d < bestD) {
          bestD = d;
          best = i;
        }
      }
    }
    if (best >= 0) charge[best] += amount;
  }

  function probe() {
    if (pointerX === null || pointerY === null) {
      probeX = null;
      probeY = null;
      return;
    }
    if (probeX === null) {
      contact(pointerX, pointerY, PROBE_CHARGE);
      probeX = pointerX;
      probeY = pointerY;
      return;
    }

    const dx = pointerX - probeX;
    const dy = pointerY - probeY;
    const dist = Math.sqrt(dx * dx + dy * dy);
    const step = spacing * PROBE_STEP;
    /* A cursor holding still injects nothing, and the distance it has covered
       is kept rather than discarded. Topping up a fixed point every frame is
       an aura that follows the mouse; each contact has to discharge and fade
       so a sweep leaves a trail behind the cursor instead of a blob under it. */
    if (dist < step) return;

    const n = Math.ceil(dist / step);
    for (let k = Math.max(1, n - PROBE_MAX + 1); k <= n; k++) {
      const u = k / n;
      contact(probeX + dx * u, probeY + dy * u, PROBE_CHARGE);
    }
    probeX = pointerX;
    probeY = pointerY;
  }

  function conduct(dt) {
    delta.fill(0);
    // Total outflow is OUT_RATE * dt * charge whatever a vertex's degree, and
    // that product stays well under 1 at the clamped dt, so no vertex can go
    // negative and the integration needs no substepping.
    const budget = OUT_RATE * dt;
    for (let e = 0; e < edgeCount; e++) {
      const src = ea[e];
      const c = charge[src];
      if (c < IGNITE) continue;
      const flow = budget * c * ew[e];
      delta[src] -= flow;
      delta[eb[e]] += flow;
    }
  }

  /** The one frame reduced motion gets: a run frozen part-way down the log. */
  function freeze() {
    const headXpx = width * HEAD_X;
    const span = Math.max(1, width * 0.55);
    for (let i = 0; i < count; i++) {
      const u = (headXpx - vx0[i]) / span;
      const run = u < 0 ? Math.exp(u * 6) : Math.exp(-u * 2.4);
      // Only the top tenth of vertices carry a strand. A gentler falloff
      // lights a third of the lattice and reads as a wash, not as a current.
      const s = vseed[i] > STRAND_CUT ? (vseed[i] - STRAND_CUT) / (1 - STRAND_CUT) : 0;
      charge[i] = run * (0.02 + 1.9 * s);
    }
  }

  /* --------------------------------------------------------------- render */

  function render(nowMs, env) {
    if (gl.isContextLost()) return;
    // A rebuild only on a real move of the dial. Comparing the raw value would
    // rebuild the whole lattice on every frame of a drag.
    const dial = Number.isFinite(env.intensity) ? clamp(env.intensity, 0, 1.4) : 1;
    if (Math.round(dial * 10) !== builtStep) build(dial);
    if (count === 0 || edgeCount === 0) return;

    const reduced = env.reduced === true;
    // Reduced motion must land on the same frame whatever the clock reads.
    const t = reduced ? 0 : nowMs / 1000;
    // A backgrounded tab returns a large delta; the clamp keeps one frame from
    // integrating a minute of conduction.
    const dt =
      reduced || lastMs === null ? 0 : Math.min((nowMs - lastMs) / 1000, 0.05);
    lastMs = nowMs;

    const head = log.head;
    const headXpx = width * HEAD_X;
    const slowestX = xOfPos(log.slowest, headXpx, head);
    const readers = log.readers;
    const rx0 = xOfPos(readers[0].pos, headXpx, head);
    const rx1 = xOfPos(readers[1].pos, headXpx, head);
    const rx2 = xOfPos(readers[2].pos, headXpx, head);
    const drain = DRAIN_R * spacing;

    const ds = DRIFT_SCALE / spacing;
    const amp = DRIFT_AMP * spacing;
    const decay = Math.exp(-DECAY * dt);
    const sinkLive = Math.max(0, 1 - ABSORB_LIVE * dt);
    const sinkLag = Math.max(0, 1 - ABSORB_LAG * dt);
    const sinkReplay = Math.max(0, 1 - ABSORB_REPLAY * dt);

    // --- positions, decay, drains ----------------------------------------
    // Position is drift and nothing else. The cursor is handled entirely in
    // the charge field; see the header.
    for (let i = 0; i < count; i++) {
      const bx = vx0[i];
      const by = vy0[i];
      const x = bx + noise(bx * ds, by * ds + t * DRIFT_SPEED) * amp;
      px[i] = x;
      py[i] = by + noise(bx * ds + 57.3, by * ds - t * DRIFT_SPEED) * amp;

      if (reduced) continue;
      let c = charge[i] * decay;
      if (Math.abs(x - rx0) < drain) c *= sinkLive;
      if (Math.abs(x - rx1) < drain) c *= sinkLag;
      if (Math.abs(x - rx2) < drain) c *= sinkReplay;
      charge[i] = c;
    }

    if (reduced) {
      freeze();
    } else {
      inject();
      probe();
      conduct(dt);
      for (let i = 0; i < count; i++) {
        const c = charge[i] + delta[i];
        charge[i] = c > CHARGE_MAX ? CHARGE_MAX : c < 0 ? 0 : c;
      }
    }

    // --- per-vertex ink ---------------------------------------------------
    const bandLo = Math.min(slowestX, headXpx);
    const bandHi = Math.max(slowestX, headXpx);
    for (let i = 0; i < count; i++) {
      const c = charge[i];
      let f = c / (c + KNEE);
      const x = px[i];
      if (x >= bandLo && x <= bandHi && f < F_BACKLOG) f = F_BACKLOG;
      if (
        (Math.abs(x - rx0) < drain || Math.abs(x - rx1) < drain) &&
        f < F_READER
      ) {
        f = F_READER;
      }
      let tint = 0;
      if (Math.abs(x - rx2) < drain) {
        tint = 1;
        if (f < F_REPLAY) f = F_REPLAY;
      }
      const top = tint === 1 ? A_FILL_MAX : A_ACCENT_MAX;
      valpha[i] = A_REST + (top - A_REST) * (f > 1 ? 1 : f);
      vtint[i] = tint;
    }

    // --- geometry ---------------------------------------------------------
    let o = 0;
    for (let e = 0; e < edgeCount; e++) {
      const a = ea[e];
      const b = eb[e];
      const ax = px[a];
      const ay = py[a];
      const bx = px[b];
      const by = py[b];
      const dx = bx - ax;
      const dy = by - ay;
      // Drift can never collapse an edge to zero, but a NaN here would poison
      // the whole buffer, so the length is floored rather than trusted.
      const len = Math.max(1e-3, Math.sqrt(dx * dx + dy * dy));
      const nx = (-dy / len) * HALF_W;
      const ny = (dx / len) * HALF_W;
      const aa = valpha[a];
      const bb = valpha[b];
      const at = vtint[a];
      const bt = vtint[b];

      data[o] = ax + nx; data[o + 1] = ay + ny; data[o + 2] = aa; data[o + 3] = at;
      data[o + 4] = ax - nx; data[o + 5] = ay - ny; data[o + 6] = aa; data[o + 7] = at;
      data[o + 8] = bx + nx; data[o + 9] = by + ny; data[o + 10] = bb; data[o + 11] = bt;
      data[o + 12] = bx + nx; data[o + 13] = by + ny; data[o + 14] = bb; data[o + 15] = bt;
      data[o + 16] = ax - nx; data[o + 17] = ay - ny; data[o + 18] = aa; data[o + 19] = at;
      data[o + 20] = bx - nx; data[o + 21] = by - ny; data[o + 22] = bb; data[o + 23] = bt;
      o += 24;
    }

    // --- draw -------------------------------------------------------------
    gl.viewport(0, 0, canvas.width, canvas.height);
    gl.disable(gl.DEPTH_TEST);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
    gl.clearColor(0, 0, 0, 0);
    gl.clear(gl.COLOR_BUFFER_BIT);

    gl.useProgram(program);
    gl.uniform2f(uResolution, width, height);
    gl.uniform3f(uAccent, colors.accentRgb[0], colors.accentRgb[1], colors.accentRgb[2]);
    gl.uniform3f(uFill, colors.fillRgb[0], colors.fillRgb[1], colors.fillRgb[2]);

    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.bufferSubData(gl.ARRAY_BUFFER, 0, data);
    gl.enableVertexAttribArray(locPos);
    gl.vertexAttribPointer(locPos, 2, gl.FLOAT, false, 16, 0);
    gl.enableVertexAttribArray(locAlpha);
    gl.vertexAttribPointer(locAlpha, 1, gl.FLOAT, false, 16, 8);
    gl.enableVertexAttribArray(locTint);
    gl.vertexAttribPointer(locTint, 1, gl.FLOAT, false, 16, 12);
    gl.drawArrays(gl.TRIANGLES, 0, edgeCount * 6);
  }

  build(1);

  return {
    render,
    resize(w, h) {
      width = Math.max(1, w);
      height = Math.max(1, h);
      if (gl.isContextLost()) return;
      build(builtStep / 10);
    },
    pointer(x, y) {
      pointerX = x;
      pointerY = y;
    },
    destroy() {
      if (gl.isContextLost()) return;
      gl.deleteBuffer(buffer);
      gl.deleteProgram(program);
    },
  };
}
