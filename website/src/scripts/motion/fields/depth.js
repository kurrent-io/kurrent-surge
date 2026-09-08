/* Deep Lattice — the log drawn as a volume rather than a plane.

   A three-dimensional lattice the camera looks into. Charge enters at the
   "Surge" wordmark, travels the edges downhill through the volume, and is
   drained by the three readers, which sit at increasing depth so a lagging
   reader is literally further away. Near vertices are bright and widely
   spaced, far ones are faint and crowded, and a slow camera drift slides the
   near layers against the far ones.

   This is the one thing the 2D field cannot do at any price, and the only
   reason a rendering dependency is here at all. */

import * as THREE from "three";
import { clamp, lerp, makeNoise2D, mulberry32 } from "../field-kit.js";

export const meta = { id: "depth", title: "Deep Lattice", engine: "three" };

/* ------------------------------------------------------------------ scene */

const FOV = 42;
const CAM_Z = 520;
const Z_NEAR = 150; // lattice extends this far toward the camera
const Z_FAR = -560; // and this far behind the focal plane

const SPACING = 84; // world units between lattice nodes at intensity 1
const JITTER = 0.34; // fraction of spacing
const LAYERS_MIN = 5;
const LAYERS_MAX = 8;

const MAX_VERTICES = 1600; // hard cap; a 4K panel must not quadruple the cost

/* ------------------------------------------------------------------ charge */

/* Charge is pushed downstream along a potential rather than diffused down a
   gradient. Diffusion equalises and stops — a packet halves at every hop and
   dies two cells out. Pushing empties a node into the next one, which is what
   makes a run travel. */
/* Both rates are per SECOND, not per frame. Expressed per frame they are tied
   to the display: the same constants ran a current to a standstill in eight
   frames on a 60Hz panel and four on a 120Hz one. A packet has to outlive its
   own travel or nothing appears to move — decay must be slower than the hop
   rate, which is the whole difference between a lattice that conducts and one
   that flickers at the injection point. */
const HOP_RATE = 17; // hops per second a run advances
const TAU_CHARGE = 1.6; // seconds to 1/e — a run has to outlive its own travel
const PATH_EXP = 3; // outflow share ∝ weight^3 — filamentary, but not a single thread
const IGNITE = 0.0025; // below this a node is treated as dark

const APPEND_CHARGE = 3.4;
const APPEND_SPREAD = 3; // nodes seeded per append, so a run starts as a front
const PROBE_CHARGE = 0.7;
const DRAIN = 0.3; // fraction a reader leaves behind when it absorbs

const REC_UNITS = 26; // world units per record along the potential axis

/* ------------------------------------------------------------------- ink */

/* DESIGN.md records 0.45 as the per-element alpha ceiling, and the resting
   lattice still obeys it. Travelling current does not: it is the one element
   the reviewer has repeatedly asked to be louder, and a run capped at the same
   value as a lit resting node cannot read as current at all. The deviation is
   deliberate and lives in exactly one constant. */
const A_MAX = 0.45; // resting structure
const A_MAX_CHARGE = 0.72; // a live run
const A_REST = 0.075;
const A_BACKLOG_GAIN = 0.35; // proportional lift for the un-consumed ring
const A_CHARGE = 0.66; // added by a travelling run

/* Depth attenuation. Far nodes fade rather than fog: fog lerps toward a
   colour, and the canvas is transparent, so a fogged line would tint toward
   whatever the page shows through instead of receding. */
const DEPTH_DIM = 0.55; // alpha multiplier at the far plane

const DRIFT_SPEED = 0.055;
const DRIFT_AMP = 0.42; // fraction of spacing

/* The pointer never moves geometry. It samples one contact per this many CSS
   pixels of travel, so a fast sweep lays a continuous run of current and a
   held cursor fires once and stops. */
const PROBE_STEP = 26;

/* Live knobs. Every one of these was a constant I was changing by hand between
   reloads; exposing them lets the value be judged by eye in one pass instead of
   a round trip per guess. */
export const params = [
  {
    key: "current",
    label: "Current",
    min: 0.2,
    max: 3,
    step: 0.05,
    value: 1,
    hint: "The main strength dial. Scales the charge injected at each append and how bright a run burns. Raise this first.",
  },
  {
    key: "peak",
    label: "Peak alpha",
    min: 0.2,
    max: 1,
    step: 0.02,
    value: A_MAX_CHARGE,
    hint: "Alpha ceiling for a live run. DESIGN.md records 0.45 as the system's limit — anything above it is a deliberate deviation.",
  },
  {
    key: "speed",
    label: "Flow speed",
    min: 3,
    max: 44,
    step: 1,
    value: HOP_RATE,
    hint: "Lattice nodes a run advances per second. Higher carries current further across the volume before it decays.",
  },
  {
    key: "life",
    label: "Charge life",
    min: 0.3,
    max: 4,
    step: 0.1,
    value: TAU_CHARGE,
    hint: "Seconds a charge packet survives. Must outlast its own travel — set it below the crossing time and current dies at the wordmark instead of flowing.",
  },
  {
    key: "rest",
    label: "Lattice rest",
    min: 0.01,
    max: 0.3,
    step: 0.005,
    value: A_REST,
    hint: "Alpha of the unlit lattice. Lower gives the current more contrast to work against; too low and the structure vanishes between runs.",
  },
  {
    key: "spread",
    label: "Branch spread",
    min: 1,
    max: 6,
    step: 0.5,
    value: PATH_EXP,
    hint: "How sharply charge prefers its strongest edge. High keeps a run to one filament; low lets it spread into a wash. Rebuilds the lattice.",
  },
];

export function create(setup) {
  const { canvas, colors, log } = setup;

  const tune = {};
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
  // updateStyle false: the host owns the CSS box and will fight the renderer
  // over it on every resize otherwise.
  renderer.setSize(width, height, false);
  renderer.setClearAlpha(0);

  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(FOV, width / height, 10, 4000);
  camera.position.set(0, 0, CAM_Z);
  camera.lookAt(0, 0, 0);

  const accent = new THREE.Color(colors.accent);
  const fill = new THREE.Color(colors.fill);

  const noise = makeNoise2D(0x5e13);

  let geometry = null;
  let material = null;
  let lines = null;

  let nodes = null; // { hx, hy, hz, x, y, z, p, charge, next, drain, replay }
  let edges = null; // { a, b, w } directed a -> b, downhill
  let outIndex = null; // per node: list of edge indices leaving it
  let positions = null;
  let colorAttr = null;

  let headNode = 0;
  let readerNodes = [0, 0, 0];
  let headP = 0;
  let slowestP = 0;

  let pointerX = null;
  let pointerY = null;
  let lastProbeX = null;
  let lastProbeY = null;

  let camOffsetX = 0;
  let camOffsetY = 0;

  const screenX = [];
  const screenY = [];
  const projected = new THREE.Vector3();
  const origin = new THREE.Vector3();

  /* --------------------------------------------------------------- build */

  function build() {
    dispose();

    const halfH = Math.tan((FOV / 2) * (Math.PI / 180)) * CAM_Z;
    const halfW = halfH * (width / height);

    // The lattice is wider than the focal-plane frustum because near layers
    // sit in front of it and would otherwise show their cut edges.
    const spanX = halfW * 2.9;
    const spanY = halfH * 2.6;

    const spacing = SPACING / (0.78 + 0.42 * clamp(intensityRef, 0.2, 1.4));
    let nx = clamp(Math.round(spanX / spacing), 6, 26);
    let ny = clamp(Math.round(spanY / spacing), 4, 18);
    let nz = clamp(
      Math.round(LAYERS_MIN + (LAYERS_MAX - LAYERS_MIN) * clamp(intensityRef / 1.4, 0, 1)),
      LAYERS_MIN,
      LAYERS_MAX
    );

    // Trim the in-plane grid before the depth layers: losing a layer costs the
    // whole effect, losing a column costs almost nothing.
    while (nx * ny * nz > MAX_VERTICES) {
      if (nx >= ny) nx--;
      else ny--;
      if (nx <= 6 && ny <= 4) break;
    }

    const rand = mulberry32(0x9e3779b9);
    const count = nx * ny * nz;

    nodes = new Array(count);
    positions = new Float32Array(count * 3);

    const index = (i, j, k) => (k * ny + j) * nx + i;

    for (let k = 0; k < nz; k++) {
      const z = lerp(Z_NEAR, Z_FAR, nz === 1 ? 0 : k / (nz - 1));
      for (let j = 0; j < ny; j++) {
        for (let i = 0; i < nx; i++) {
          const fx = nx === 1 ? 0.5 : i / (nx - 1);
          const fy = ny === 1 ? 0.5 : j / (ny - 1);
          const hx = (fx - 0.5) * spanX + (rand() - 0.5) * spacing * JITTER * 2;
          const hy = (fy - 0.5) * spanY + (rand() - 0.5) * spacing * JITTER * 2;
          const hz = z + (rand() - 0.5) * spacing * JITTER;
          const n = {
            hx,
            hy,
            hz,
            x: hx,
            y: hy,
            z: hz,
            phase: rand() * Math.PI * 2,
            p: 0,
            charge: 0,
            next: 0,
            drain: 0,
            replay: false,
          };
          nodes[index(i, j, k)] = n;
        }
      }
    }

    /* The potential is RADIAL from the wordmark, not a left-to-right axis.

       An axial potential sends every run in one direction, so the far side of
       the lattice never carries current and the field reads as bright on one
       half and dead on the other — which is exactly what it did. Radially,
       charge leaves the wordmark in every direction and the whole volume
       conducts. Readers become rings at increasing radius, so a lagging reader
       is still further away; it is simply further in all directions at once. */
    const originScreenX = width * 0.42;
    const originScreenY = height * 0.2;
    const halfHOrigin = Math.tan((FOV / 2) * (Math.PI / 180)) * CAM_Z;
    origin.set(
      (originScreenX / width - 0.5) * 2 * halfHOrigin * (width / height),
      -(originScreenY / height - 0.5) * 2 * halfHOrigin,
      Z_NEAR * 0.5
    );

    for (const n of nodes) {
      n.p = -Math.hypot(n.hx - origin.x, n.hy - origin.y, (n.hz - origin.z) * 0.7);
    }

    const raw = [];
    for (let k = 0; k < nz; k++) {
      for (let j = 0; j < ny; j++) {
        for (let i = 0; i < nx; i++) {
          const a = index(i, j, k);
          if (i + 1 < nx) raw.push(a, index(i + 1, j, k));
          if (j + 1 < ny) raw.push(a, index(i, j + 1, k));
          if (k + 1 < nz) raw.push(a, index(i, j, k + 1));
          // One sparse diagonal per cell keeps the volume from reading as a
          // stack of flat grids when the camera is near head-on.
          if (i + 1 < nx && j + 1 < ny && ((i + j + k) & 3) === 0) {
            raw.push(a, index(i + 1, j + 1, k));
          }
        }
      }
    }

    const edgeCount = raw.length / 2;
    edges = new Array(edgeCount);
    outIndex = new Array(count);
    for (let v = 0; v < count; v++) outIndex[v] = [];

    for (let e = 0; e < edgeCount; e++) {
      let a = raw[e * 2];
      let b = raw[e * 2 + 1];
      // Orient every edge downhill once, at build time. Conduction is a push,
      // so the direction has to be fixed or charge sloshes back and forth.
      if (nodes[a].p < nodes[b].p) {
        const t = a;
        a = b;
        b = t;
      }
      const w = 0.35 + 0.65 * mulberry32(e * 2654435761)();
      edges[e] = { a, b, w, share: 0 };
      outIndex[a].push(e);
    }

    // Normalise each node's outflow so a packet is not divided evenly among six
    // neighbours — an even split falls under IGNITE within three hops. The
    // fourth power is what keeps a run a filament instead of a wash.
    for (let v = 0; v < count; v++) {
      const list = outIndex[v];
      let total = 0;
      for (const e of list) total += Math.pow(edges[e].w, tune.spread);
      if (total <= 0) continue;
      for (const e of list) edges[e].share = Math.pow(edges[e].w, tune.spread) / total;
    }

    const indices = new Uint16Array(edgeCount * 2);
    for (let e = 0; e < edgeCount; e++) {
      indices[e * 2] = edges[e].a;
      indices[e * 2 + 1] = edges[e].b;
    }

    geometry = new THREE.BufferGeometry();
    geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));
    // Four components: three.js applies the fourth as alpha on a vertex-colour
    // attribute, which is the only way to fade a line to nothing over a
    // transparent canvas. Scaling RGB toward black instead would paint dark
    // lines over the page ground rather than fading out.
    colorAttr = new THREE.BufferAttribute(new Float32Array(count * 4), 4);
    geometry.setAttribute("color", colorAttr);
    geometry.setIndex(new THREE.BufferAttribute(indices, 1));

    material = new THREE.LineBasicMaterial({
      vertexColors: true,
      transparent: true,
      depthWrite: false,
    });

    lines = new THREE.LineSegments(geometry, material);
    lines.frustumCulled = false;
    scene.add(lines);

    for (const n of nodes) {
      n.charge = 0;
      n.next = 0;
    }
    writePositions(0);
  }

  function dispose() {
    if (lines) scene.remove(lines);
    if (geometry) geometry.dispose();
    if (material) material.dispose();
    geometry = null;
    material = null;
    lines = null;
  }

  /* ------------------------------------------------------------- geometry */

  function writePositions(t) {
    for (let v = 0; v < nodes.length; v++) {
      const n = nodes[v];
      const a = DRIFT_AMP * SPACING;
      n.x = n.hx + noise(n.hx * 0.0016 + t * DRIFT_SPEED, n.hy * 0.0016) * a;
      n.y = n.hy + noise(n.hy * 0.0016, n.hz * 0.0016 + t * DRIFT_SPEED) * a;
      n.z = n.hz + noise(n.hz * 0.0016 + t * DRIFT_SPEED * 0.7, n.hx * 0.0016) * a * 0.6;
      positions[v * 3] = n.x;
      positions[v * 3 + 1] = n.y;
      positions[v * 3 + 2] = n.z;
    }
    geometry.attributes.position.needsUpdate = true;
  }

  function project() {
    for (let v = 0; v < nodes.length; v++) {
      const n = nodes[v];
      projected.set(n.x, n.y, n.z).project(camera);
      screenX[v] = (projected.x * 0.5 + 0.5) * width;
      screenY[v] = (-projected.y * 0.5 + 0.5) * height;
    }
  }

  function nearestToScreen(px, py) {
    let best = -1;
    let bestD = Infinity;
    for (let v = 0; v < nodes.length; v++) {
      const dx = screenX[v] - px;
      const dy = screenY[v] - py;
      const d = dx * dx + dy * dy;
      if (d < bestD) {
        bestD = d;
        best = v;
      }
    }
    return best;
  }

  function nearestToPotential(target, preferDepth) {
    let best = -1;
    let bestD = Infinity;
    for (let v = 0; v < nodes.length; v++) {
      const n = nodes[v];
      const d = Math.abs(n.p - target) + Math.abs(n.hz - preferDepth) * 0.35;
      if (d < bestD) {
        bestD = d;
        best = v;
      }
    }
    return best;
  }

  /* ---------------------------------------------------------------- state */

  let intensityRef = setup.intensityAtBuild ?? 1;
  let builtIntensity = intensityRef;
  let lastMs = null;

  build();

  /* --------------------------------------------------------------- render */

  function render(nowMs, env) {
    const t = nowMs / 1000;
    const reduced = Boolean(env.reduced);

    if (env.intensity !== builtIntensity) {
      intensityRef = env.intensity;
      builtIntensity = env.intensity;
      build();
    }

    const dt = lastMs === null ? 0 : Math.min((nowMs - lastMs) / 1000, 0.05);
    lastMs = nowMs;

    if (!reduced) {
      writePositions(t);

      // Slow lissajous drift. Both position and target move, so near layers
      // slide against far ones; a camera that only orbits reads as the whole
      // page swaying instead.
      const cx = Math.sin(t * 0.11) * 46 + camOffsetX;
      const cy = Math.cos(t * 0.083) * 30 + camOffsetY;
      camera.position.set(cx, cy, CAM_Z + Math.sin(t * 0.07) * 26);
      camera.lookAt(Math.sin(t * 0.05) * 18, Math.cos(t * 0.06) * 12, 0);
    }
    camera.updateMatrixWorld();
    project();

    /* --- where the log enters ------------------------------------------ */

    // The head is pinned to the wordmark: the current appears to leave the
    // product name and run back into the volume.
    headNode = nearestToScreen(env.anchor.x, env.anchor.y);
    headP = nodes[headNode].p;

    const readers = log.readers;
    slowestP = Infinity;
    for (let i = 0; i < readers.length; i++) {
      const behind = log.head - readers[i].pos;
      const targetP = headP - behind * REC_UNITS;
      // Each reader sits deeper than the last, so lag reads as distance into
      // the picture as well as across it.
      const depth = lerp(Z_NEAR * 0.4, Z_FAR * 0.75, i / (readers.length - 1));
      const v = nearestToPotential(targetP, depth);
      readerNodes[i] = v;
      nodes[v].replay = readers[i].kind === "replay";
      if (nodes[v].p < slowestP) slowestP = nodes[v].p;
    }

    /* --- charge --------------------------------------------------------- */

    if (!reduced) {
      const appends = log.drainAppends();
      if (appends > 0) {
        const packet = APPEND_CHARGE * tune.current * Math.min(appends, 3);
        nodes[headNode].charge += packet;
        // Seed the first hop too. A single node injection leaves the run one
        // filament wide at the wordmark, which reads as a spark rather than as
        // current entering the lattice.
        const out = outIndex[headNode];
        for (let i = 0; i < Math.min(APPEND_SPREAD, out.length); i++) {
          nodes[edges[out[i]].b].charge += packet * 0.45;
        }
      }

      /* One push step. The fraction is capped below 1: a node may never empty
         completely in a single frame or the run tears into disconnected dots
         on a slow frame. */
      const moveFrac = clamp(tune.speed * dt, 0, 0.85);
      for (const n of nodes) n.next = 0;
      for (let e = 0; e < edges.length; e++) {
        const edge = edges[e];
        const src = nodes[edge.a];
        if (src.charge < IGNITE) continue;
        const moved = src.charge * moveFrac * edge.share;
        src.next -= moved;
        nodes[edge.b].next += moved;
      }
      const decay = Math.exp(-dt / tune.life);
      for (const n of nodes) {
        n.charge = (n.charge + n.next) * decay;
        if (n.charge < IGNITE) n.charge = 0;
      }

      // Readers absorb. This is what makes the backlog drain when one catches
      // up rather than the whole volume simply fading.
      for (const v of readerNodes) nodes[v].charge *= DRAIN;
    }

    /* --- ink ------------------------------------------------------------ */

    const array = colorAttr.array;
    const zSpan = Z_NEAR - Z_FAR;
    const backlogLo = Math.min(slowestP, headP);
    const backlogHi = Math.max(slowestP, headP);

    for (let v = 0; v < nodes.length; v++) {
      const n = nodes[v];

      const depthT = clamp((n.z - Z_FAR) / zSpan, 0, 1);
      const depthMul = lerp(DEPTH_DIM, 1, depthT);

      /* The backlog lift is a PROPORTION of the resting alpha, not a fixed
         addition. Added flat it was larger than the rest value itself once the
         rest slider went low, which split the lattice into a lit half and a
         dark half along the band's edge — a hard seam, not a field. */
      let a = tune.rest;
      if (n.p >= backlogLo && n.p <= backlogHi) a *= 1 + A_BACKLOG_GAIN;
      a = Math.min(A_MAX, a);

      // A square root on the charge term so a node lights well before it is
      // saturated; a linear ramp left most of a run's length dim while only
      // its head reached full strength.
      const lit = A_CHARGE * tune.current * Math.sqrt(Math.min(1, n.charge));
      a = Math.min(tune.peak, a + lit) * depthMul;

      const c = n.replay ? fill : accent;
      array[v * 4] = c.r;
      array[v * 4 + 1] = c.g;
      array[v * 4 + 2] = c.b;
      array[v * 4 + 3] = a;

      n.replay = false;
    }
    colorAttr.needsUpdate = true;

    renderer.render(scene, camera);
  }

  /* -------------------------------------------------------------- inputs */

  return {
    render,

    setParam(key, value) {
      if (!(key in tune)) return;
      tune[key] = value;
      // Only the outflow shares are baked at build time; everything else is
      // read per frame, so a rebuild is the exception rather than the rule.
      if (key === "spread") build();
    },

    resize(w, h, nextDpr) {
      width = w;
      height = h;
      dpr = nextDpr;
      renderer.setPixelRatio(dpr);
      renderer.setSize(width, height, false);
      camera.aspect = width / height;
      camera.updateProjectionMatrix();
      build();
    },

    pointer(x, y) {
      if (x === null || y === null) {
        pointerX = null;
        pointerY = null;
        lastProbeX = null;
        lastProbeY = null;
        camOffsetX = 0;
        camOffsetY = 0;
        return;
      }
      pointerX = x;
      pointerY = y;

      // Camera parallax only. No vertex is displaced by the pointer — a mesh
      // that leans toward the cursor reads as a ripple in water, which is
      // exactly what this field must not look like.
      camOffsetX = (x / width - 0.5) * 34;
      camOffsetY = -(y / height - 0.5) * 22;

      if (lastProbeX === null) {
        lastProbeX = x;
        lastProbeY = y;
        probeAt(x, y);
        return;
      }

      // Sample by distance travelled, not per event, so the trail follows the
      // cursor's speed rather than the pointer event rate.
      let dx = x - lastProbeX;
      let dy = y - lastProbeY;
      let dist = Math.hypot(dx, dy);
      let guard = 0;
      while (dist >= PROBE_STEP && guard++ < 16) {
        const k = PROBE_STEP / dist;
        lastProbeX += dx * k;
        lastProbeY += dy * k;
        probeAt(lastProbeX, lastProbeY);
        dx = x - lastProbeX;
        dy = y - lastProbeY;
        dist = Math.hypot(dx, dy);
      }
    },

    destroy() {
      dispose();
      // A leaked context per field swap exhausts the browser after a dozen
      // switches; the renderer will not release it on its own.
      renderer.dispose();
      renderer.forceContextLoss();
    },
  };

  function probeAt(px, py) {
    if (!nodes || !screenX.length) return;
    const v = nearestToScreen(px, py);
    if (v >= 0) nodes[v].charge += PROBE_CHARGE;
  }
}
