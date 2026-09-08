/* The motion field host.

   Owns the clock, the canvas, the pointer, and the reach mask, so a field
   module owns nothing but its own drawing. Swapping fields tears the canvas
   down and builds a new one: a 2D and a WebGL context cannot share an element,
   and `transferControlToOffscreen` is one-way, so the lab runs every field on
   the main thread. The tiered worker path returns when a winner lands on the
   real landing page.

   ── The field contract ────────────────────────────────────────────────────
   A field module exports:

     export const meta = { id, title, engine: "2d" | "webgl" };
     export function create(setup) -> field

   setup = {
     canvas,            HTMLCanvasElement, already sized to width*dpr
     ctx | gl,          the 2D context, or a WebGL2/WebGL context
     width, height,     CSS pixels
     dpr,               device pixel ratio, capped at 2
     colors,            { accent, fill, deep, bg, border } as CSS strings,
                        plus accentRgb / fillRgb / deepRgb / bgRgb in 0..1
     log,               the shared log model (see log-model.js)
   }

   field = {
     render(nowMs, env),          env is described below. The field clears or
                                  trails as it likes; the host never clears.
     resize(width, height, dpr),  canvas backing store is already resized
     pointer(x, y),               CSS pixels in viewport space, or (null, null)
     destroy(),                   release GPU objects and timers
   }

   env = {
     intensity,   0..1.4 user dial
     reduced,     prefers-reduced-motion
     scrollY,     document scroll position
     viewportW/H, viewport size in CSS pixels
     heroTop,     hero rect top in viewport space
     heroBottom,  hero rect bottom in viewport space
     reach,       "hero" | "graded" | "full"
   }
   ────────────────────────────────────────────────────────────────────────── */

import { createLogModel } from "./log-model.js";
import { toRgb01 } from "./field-kit.js";

/* In development, globbed rather than listed: a named `import("./fields/x.js")`
   is resolved by Vite at transform time, so one absent field takes the whole
   page down with a resolve error, while a glob simply omits it and the host
   reports an unknown id. The motion lab needs every field for that reason.

   Production ships one. `import.meta.env.DEV` is statically replaced, so only
   the shipped branch is bundled and the retired fields emit no chunk at all —
   globbing here put 30KB of never-fetched JavaScript into dist. */
const FIELD_MODULES = import.meta.env.DEV
  ? import.meta.glob("./fields/*.js")
  : { "./fields/deeparc.js": () => import("./fields/deeparc.js") };

const FIELD_LOADERS = Object.fromEntries(
  Object.entries(FIELD_MODULES).map(([path, loader]) => [
    path.replace(/^\.\/fields\//, "").replace(/\.js$/, ""),
    loader,
  ])
);

export const FIELD_IDS = Object.keys(FIELD_LOADERS);

/** A retina panel gains nothing above 2x for 1px strokes. */
const dprOf = () => Math.min(window.devicePixelRatio || 1, 2);

/* The graded reach, in viewport heights either side of the hero's lower edge.
   The ramp starts well up inside the hero, so the falloff is already underway
   on the first screen, and reaches nothing shortly past it. */
const GRADE_LEAD = 0.6;
const GRADE_RUN = 0.5;

/* A linear alpha ramp reads slow: alpha is not perceived linearly, so the
   upper half of a two-stop gradient looks almost uniform and the fall arrives
   all at once near the end. These stops bend it into an ease-out, which is
   what makes the grade read as heavy rather than as a long even wash. */
const GRADE_STOPS = [1, 0.62, 0.34, 0.15, 0.05, 0];

function readColors() {
  const root = getComputedStyle(document.documentElement);
  const read = (name, fallback) =>
    root.getPropertyValue(name).trim() || fallback;

  const accent = read("--accent", "#60c2ff");
  const fill = read("--accent-fill", "#0077d9");
  const deep = read("--accent-deep", "#005fbd");
  const bg = read("--bg", "#0f181f");
  const border = read("--border", "rgba(180, 210, 240, 0.12)");

  return {
    accent,
    fill,
    deep,
    bg,
    border,
    accentRgb: toRgb01(accent),
    fillRgb: toRgb01(fill),
    deepRgb: toRgb01(deep),
    bgRgb: toRgb01(bg, [0.059, 0.094, 0.122]),
  };
}

export function createHost(options) {
  const mount = options.mount;
  const heroSelector = options.heroSelector ?? ".hero";
  const anchorSelector = options.anchorSelector ?? ".hero__h1--name .accent";
  const onStatus = options.onStatus ?? (() => {});

  const motion = window.matchMedia("(prefers-reduced-motion: reduce)");
  const log = createLogModel({ reduced: motion.matches });
  const colors = readColors();

  let canvas = null;
  let field = null;
  let fieldId = null;
  let generation = 0;

  let reach = options.reach ?? "hero";
  let intensity = options.intensity ?? 1;
  let width = 1;
  let height = 1;
  let pointerX = null;
  let pointerY = null;
  let raf = 0;
  let running = false;
  let hidden = false;
  /* Counts real field renders. The page's own requestAnimationFrame ticks
     whether or not the field loop is alive, so a frame rate sampled there
     reports 60+ over a completely frozen field. */
  let renders = 0;
  let forceMotion = false;
  let fieldParams = [];
  let paramValues = {};

  const env = {
    intensity,
    reduced: motion.matches,
    scrollY: 0,
    viewportW: 1,
    viewportH: 1,
    heroTop: 0,
    heroBottom: 0,
    reach,
    anchor: {
      x: 0,
      y: 0,
      left: 0,
      right: 0,
      top: 0,
      bottom: 0,
      found: false,
    },
  };

  /* ---------------------------------------------------------------- canvas */

  function buildCanvas(engine) {
    const el = document.createElement("canvas");
    el.className = "motion-field";
    el.setAttribute("aria-hidden", "true");
    mount.appendChild(el);

    let ctx = null;
    let gl = null;
    if (engine === "three") {
      /* Three.js owns its own context and its own backing store. The host
         hands over a bare element and never touches width/height on it, or
         the renderer and the host fight over the drawing buffer every
         resize. */
      return { el, ctx: null, gl: null, ownsRaster: true };
    }
    if (engine === "webgl") {
      const attrs = {
        alpha: true,
        antialias: true,
        premultipliedAlpha: true,
        powerPreference: "low-power",
      };
      gl = el.getContext("webgl2", attrs) || el.getContext("webgl", attrs);
      if (!gl) {
        el.remove();
        return null;
      }
    } else {
      ctx = el.getContext("2d", { alpha: true });
      if (!ctx) {
        el.remove();
        return null;
      }
    }
    return { el, ctx, gl };
  }

  function sizeCanvas(el, ctx, ownsRaster) {
    const dpr = dprOf();
    el.style.width = `${width}px`;
    el.style.height = `${height}px`;
    if (ownsRaster) return dpr;
    el.width = Math.max(1, Math.round(width * dpr));
    el.height = Math.max(1, Math.round(height * dpr));
    if (ctx) ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    return dpr;
  }

  function measure() {
    width = window.innerWidth;
    height = window.innerHeight;
    env.viewportW = width;
    env.viewportH = height;
  }

  /* ------------------------------------------------------------------ loop */

  let heroEl = null;
  let anchorEl = null;
  let lastMask = null;
  let lastOpacity = null;

  function readGeometry() {
    env.scrollY = window.scrollY;
    if (!heroEl || !heroEl.isConnected) heroEl = document.querySelector(heroSelector);
    if (heroEl) {
      const r = heroEl.getBoundingClientRect();
      env.heroTop = r.top;
      env.heroBottom = r.bottom;
    } else {
      env.heroTop = 0;
      env.heroBottom = height;
    }

    /* The wordmark, in viewport pixels. A field that emanates from the product
       name reads as the name doing something; the same effect fired from an
       arbitrary fraction of the width reads as wallpaper. Falls back to the
       hero's upper-left third when the element is absent, so a field can use
       the anchor unconditionally. */
    if (!anchorEl || !anchorEl.isConnected) anchorEl = document.querySelector(anchorSelector);
    if (anchorEl) {
      const r = anchorEl.getBoundingClientRect();
      env.anchor.x = r.left + r.width / 2;
      env.anchor.y = r.top + r.height / 2;
      env.anchor.left = r.left;
      env.anchor.right = r.right;
      env.anchor.top = r.top;
      env.anchor.bottom = r.bottom;
      env.anchor.found = true;
    } else {
      env.anchor.x = width * 0.3;
      env.anchor.y = env.heroTop + (env.heroBottom - env.heroTop) * 0.22;
      env.anchor.left = env.anchor.x - 120;
      env.anchor.right = env.anchor.x + 120;
      env.anchor.top = env.anchor.y - 40;
      env.anchor.bottom = env.anchor.y + 40;
      env.anchor.found = false;
    }

    applyReach();
  }

  /* Reach is a property of the canvas element, not of the field, so every field
     answers the modes identically and none reimplements it.

     Both writes are guarded against an unchanged value. Assigning `maskImage`
     invalidates style even when the string is identical, and the next frame's
     getBoundingClientRect then forces a synchronous layout — a full-viewport
     canvas mask re-rasterised once per frame is enough on its own to drag the
     loop to single-figure frame rates. */
  function applyReach() {
    if (!canvas) return;
    const el = canvas.el;
    let mask = "none";
    let opacity = "1";

    if (reach === "hero") {
      // Quantised to 2px: a sub-pixel change in the hero rect is invisible and
      // is not worth re-rasterising the mask for.
      const cut = Math.max(0, Math.round(env.heroBottom / 2) * 2);
      const fade = Math.max(0, cut - 110);
      mask = `linear-gradient(to bottom, #000 0px, #000 ${fade}px, transparent ${cut}px)`;
    } else if (reach === "graded") {
      /* A gradient down the DOCUMENT, not a uniform dim of the canvas. The
         previous version faded the whole field by one scalar as you scrolled,
         so every pixel on screen carried the same strength and the "grade" was
         only visible by scrolling — which is not what a graded field means.

         Both stops are already in viewport space because heroBottom tracks the
         scroll, so the ramp travels with the page and every screen shows a real
         top-to-bottom falloff. Quantised to 8px: the mask re-rasterises on
         change, and a per-pixel-of-scroll rewrite is the cost that froze this
         page once already. */
      const q = (n) => Math.round(n / 8) * 8;
      const start = q(env.heroBottom - height * GRADE_LEAD);
      const end = q(env.heroBottom + height * GRADE_RUN);
      const span = Math.max(1, end - start);
      const stops = GRADE_STOPS.map((a, i) => {
        const at = start + (span * i) / (GRADE_STOPS.length - 1);
        return `rgba(0,0,0,${a}) ${Math.round(at)}px`;
      });
      mask = `linear-gradient(to bottom, ${stops.join(", ")})`;
    }

    if (mask !== lastMask) {
      el.style.webkitMaskImage = mask;
      el.style.maskImage = mask;
      lastMask = mask;
    }
    if (opacity !== lastOpacity) {
      el.style.opacity = opacity;
      lastOpacity = opacity;
    }
  }

  function frame(nowMs) {
    if (!running) return;
    log.advance(nowMs);
    readGeometry();
    if (field) field.render(nowMs, env);
    renders++;
    raf = requestAnimationFrame(frame);
  }

  function start() {
    if (running || hidden || !field) return;
    running = true;
    raf = requestAnimationFrame(frame);
  }

  function stop() {
    running = false;
    if (raf) cancelAnimationFrame(raf);
    raf = 0;
  }

  /* ---------------------------------------------------------------- switch */

  async function setField(id) {
    if (!FIELD_LOADERS[id]) {
      onStatus({ id, state: "error", message: "field module not present" });
      return;
    }
    const token = ++generation;

    stop();
    if (field) {
      try {
        field.destroy();
      } catch {
        // A field that throws on teardown must not block the next one.
      }
      field = null;
    }
    if (canvas) {
      canvas.el.remove();
      canvas = null;
    }

    onStatus({ id, state: "loading" });

    let module;
    try {
      module = await FIELD_LOADERS[id]();
    } catch (error) {
      onStatus({ id, state: "error", message: String(error) });
      return;
    }
    if (token !== generation) return;

    const built = buildCanvas(module.meta.engine);
    if (!built) {
      onStatus({
        id,
        state: "error",
        message: `no ${module.meta.engine} context available`,
      });
      return;
    }
    canvas = built;
    lastMask = null;
    lastOpacity = null;

    measure();
    const dpr = sizeCanvas(built.el, built.ctx, built.ownsRaster);

    try {
      field = module.create({
        canvas: built.el,
        ctx: built.ctx,
        gl: built.gl,
        width,
        height,
        dpr,
        colors,
        log,
      });
    } catch (error) {
      built.el.remove();
      canvas = null;
      onStatus({ id, state: "error", message: String(error) });
      return;
    }

    fieldId = id;
    fieldParams = Array.isArray(module.params) ? module.params : [];
    paramValues = {};
    for (const p of fieldParams) paramValues[p.key] = p.value;
    env.reduced = motion.matches && !forceMotion;
    env.intensity = intensity;
    env.reach = reach;
    readGeometry();

    // One frame regardless of reduced motion, so the field is never blank.
    field.render(performance.now(), env);
    onStatus({ id, state: "ready", engine: module.meta.engine, title: module.meta.title });

    if (!env.reduced) start();
  }

  /* --------------------------------------------------------------- listeners */

  measure();

  let resizeFrame = 0;
  const onResize = () => {
    cancelAnimationFrame(resizeFrame);
    resizeFrame = requestAnimationFrame(() => {
      measure();
      if (!canvas || !field) return;
      const dpr = sizeCanvas(canvas.el, canvas.ctx, canvas.ownsRaster);
      field.resize(width, height, dpr);
      readGeometry();
      if (!running) field.render(performance.now(), env);
    });
  };
  window.addEventListener("resize", onResize, { passive: true });

  // The canvas is pointer-events: none, so the window owns the pointer.
  window.addEventListener(
    "pointermove",
    (event) => {
      if (event.pointerType !== "mouse") return;
      pointerX = event.clientX;
      pointerY = event.clientY;
      if (field) field.pointer(pointerX, pointerY);
    },
    { passive: true }
  );
  window.addEventListener(
    "pointerleave",
    () => {
      pointerX = null;
      pointerY = null;
      if (field) field.pointer(null, null);
    },
    { passive: true }
  );

  window.addEventListener(
    "scroll",
    () => {
      if (!running) {
        readGeometry();
        if (field) field.render(performance.now(), env);
      }
    },
    { passive: true }
  );

  document.addEventListener("visibilitychange", () => {
    hidden = document.visibilityState !== "visible";
    if (hidden) stop();
    else start();
  });

  motion.addEventListener("change", (event) => {
    const reduced = event.matches && !forceMotion;
    env.reduced = reduced;
    log.setReduced(reduced);
    if (reduced) {
      stop();
      if (field) field.render(performance.now(), env);
    } else {
      start();
    }
  });

  return {
    setField,
    get fieldId() {
      return fieldId;
    },
    /** Truth about the field loop, not about the page's own frame rate. */
    stats() {
      return { renders, running, reduced: env.reduced, osReduced: motion.matches, hidden };
    },

    /* Live tuning. A field may export `params` — a list of named knobs with a
       range and a default — and a `setParam(key, value)`. The lab renders one
       slider per entry and writes straight into the running field, so a value
       is judged by eye rather than by a round trip through an edit and a
       reload. Fields without the export simply have no knobs. */
    params() {
      return fieldParams;
    },
    setParam(key, value) {
      if (!field || typeof field.setParam !== "function") return;
      field.setParam(key, value);
      paramValues[key] = value;
      if (!running) field.render(performance.now(), env);
    },
    paramValues() {
      return { ...paramValues };
    },
    setReach(value) {
      reach = value;
      env.reach = value;
      readGeometry();
      if (!running && field) field.render(performance.now(), env);
    },
    setIntensity(value) {
      intensity = value;
      env.intensity = value;
      if (!running && field) field.render(performance.now(), env);
    },
    /* Lab-only. An OS-level "reduce motion" freezes every field by design, and
       from the outside that is indistinguishable from a field that is broken.
       This makes the two tellable apart in one keystroke. */
    setForceMotion(value) {
      forceMotion = Boolean(value);
      const reduced = motion.matches && !forceMotion;
      env.reduced = reduced;
      log.setReduced(reduced);
      if (reduced) {
        stop();
        if (field) field.render(performance.now(), env);
      } else {
        start();
      }
      return { reduced, osReduced: motion.matches };
    },
    destroy() {
      stop();
      if (field) field.destroy();
      if (canvas) canvas.el.remove();
    },
  };
}
