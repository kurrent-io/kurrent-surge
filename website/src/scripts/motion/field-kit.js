/* Shared primitives for the landing page's motion fields.

   Five fields draw the same log model in five different ways. Everything here
   exists so they cannot disagree about a colour, a random sequence, or how a
   1px stroke lands on a device pixel. A field that needs one of these imports
   it; it does not roll its own. */

/* ------------------------------------------------------------------ colour */

/** `#60c2ff` or `rgb(96, 194, 255)` to `[r, g, b]` in 0..1. */
export function toRgb01(value, fallback = [0.376, 0.761, 1]) {
  if (typeof value !== "string") return fallback;

  const hex = value.trim().match(/^#([0-9a-f]{3}|[0-9a-f]{6})$/i);
  if (hex) {
    const h = hex[1];
    const full = h.length === 3 ? h.replace(/./g, (c) => c + c) : h;
    return [
      parseInt(full.slice(0, 2), 16) / 255,
      parseInt(full.slice(2, 4), 16) / 255,
      parseInt(full.slice(4, 6), 16) / 255,
    ];
  }

  const fn = value.match(/-?\d*\.?\d+/g);
  if (fn && fn.length >= 3) {
    return [Number(fn[0]) / 255, Number(fn[1]) / 255, Number(fn[2]) / 255];
  }
  return fallback;
}

/** `rgba(r, g, b, a)` from a token string, for Canvas 2D fills. */
export function rgba(value, alpha) {
  const [r, g, b] = toRgb01(value);
  return `rgba(${Math.round(r * 255)}, ${Math.round(g * 255)}, ${Math.round(
    b * 255
  )}, ${alpha})`;
}

/* ------------------------------------------------------------------ raster */

/** Snap to a half-pixel so a 1px stroke lands on one device pixel. */
export const crisp = (n) => Math.round(n) + 0.5;

export const clamp = (n, lo, hi) => (n < lo ? lo : n > hi ? hi : n);
export const lerp = (a, b, t) => a + (b - a) * t;
export const smoothstep = (t) => t * t * (3 - 2 * t);

/* ------------------------------------------------------------------ random */

/** Deterministic PRNG. A field that reseeds on resize must not reshuffle. */
export function mulberry32(seed) {
  let a = seed >>> 0;
  return function () {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** Smooth 2D value noise in roughly -1..1. Cheap, and enough for drift. */
export function makeNoise2D(seed = 1337) {
  const rand = mulberry32(seed);
  const perm = new Uint8Array(512);
  const source = new Uint8Array(256);
  for (let i = 0; i < 256; i++) source[i] = i;
  for (let i = 255; i > 0; i--) {
    const j = Math.floor(rand() * (i + 1));
    const t = source[i];
    source[i] = source[j];
    source[j] = t;
  }
  for (let i = 0; i < 512; i++) perm[i] = source[i & 255];

  const grad = (hash, x, y) => {
    const h = hash & 3;
    const u = h < 2 ? x : y;
    const v = h < 2 ? y : x;
    return ((h & 1) === 0 ? u : -u) + ((h & 2) === 0 ? v : -v);
  };

  return function noise2D(x, y) {
    const xi = Math.floor(x) & 255;
    const yi = Math.floor(y) & 255;
    const xf = x - Math.floor(x);
    const yf = y - Math.floor(y);
    const u = smoothstep(xf);
    const v = smoothstep(yf);

    const aa = perm[perm[xi] + yi];
    const ab = perm[perm[xi] + yi + 1];
    const ba = perm[perm[xi + 1] + yi];
    const bb = perm[perm[xi + 1] + yi + 1];

    const x1 = lerp(grad(aa, xf, yf), grad(ba, xf - 1, yf), u);
    const x2 = lerp(grad(ab, xf, yf - 1), grad(bb, xf - 1, yf - 1), u);
    return lerp(x1, x2, v);
  };
}

/* ------------------------------------------------------------------- webgl */

export function compileProgram(gl, vertexSource, fragmentSource) {
  const compile = (type, source) => {
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
      const log = gl.getShaderInfoLog(shader);
      gl.deleteShader(shader);
      throw new Error(`shader: ${log}`);
    }
    return shader;
  };

  const vs = compile(gl.VERTEX_SHADER, vertexSource);
  const fs = compile(gl.FRAGMENT_SHADER, fragmentSource);
  const program = gl.createProgram();
  gl.attachShader(program, vs);
  gl.attachShader(program, fs);
  gl.linkProgram(program);
  gl.deleteShader(vs);
  gl.deleteShader(fs);

  if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
    const log = gl.getProgramInfoLog(program);
    gl.deleteProgram(program);
    throw new Error(`link: ${log}`);
  }
  return program;
}

/** A clip-space triangle pair on attribute location 0. */
export function createFullscreenQuad(gl) {
  const buffer = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
  gl.bufferData(
    gl.ARRAY_BUFFER,
    new Float32Array([-1, -1, 3, -1, -1, 3]),
    gl.STATIC_DRAW
  );
  return {
    buffer,
    bind(location) {
      gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
      gl.enableVertexAttribArray(location);
      gl.vertexAttribPointer(location, 2, gl.FLOAT, false, 0, 0);
    },
    draw() {
      gl.drawArrays(gl.TRIANGLES, 0, 3);
    },
    destroy() {
      gl.deleteBuffer(buffer);
    },
  };
}
