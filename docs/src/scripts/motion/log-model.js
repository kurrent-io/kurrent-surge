/* The product's semantics, rendered engine-agnostic.

   Every motion field on the landing page draws this same model, so the
   background is never decoration: it is records appended to a log and three
   readers consuming it at different rates. Extracted from the retired hero log strip so a
   WebGL field and a Canvas 2D field cannot drift apart on what the numbers
   mean.

   Positions are fractional record indices, not pixels. A field decides where
   record `n` lands on screen; the model only says which records exist, which
   are still work, and how recently each one arrived. */

export const APPEND_RATE = 2.2; // records per second
export const INDEX_EVERY = 10; // every Nth record is an index entry

/* Recency decays over the time the next few records take to land, so a fresh
   append reads as an event rather than as a permanently brighter tick. */
const RECENCY_TAU = 0.7; // in records, not seconds

export function createLogModel(options = {}) {
  const appendRate = options.appendRate ?? APPEND_RATE;

  let head = 60; // start mid-stream; an empty log is a worse first frame
  let replayPos = head - 30;
  let lastMs = null;
  let reduced = Boolean(options.reduced);

  /* Discrete fields (arcs, wavefronts) fire once per append rather than
     sampling a continuous position, so completed appends queue here and the
     field drains them on its own frame. */
  let pendingAppends = 0;
  let lastWholeRecord = Math.floor(head);

  const readers = [
    { pos: head, kind: "live" },
    { pos: head, kind: "lagging" },
    { pos: replayPos, kind: "replay" },
  ];

  function advance(nowMs) {
    const t = nowMs / 1000;

    if (lastMs !== null && !reduced) {
      // A backgrounded tab returns a large delta; clamp it so the log does not
      // leap forward by a minute of appends on the first frame back.
      const dt = Math.min((nowMs - lastMs) / 1000, 0.05);
      head += appendRate * dt;
      replayPos += appendRate * 1.9 * dt;
      if (replayPos > head - 1.5) replayPos = head - 30;
    }
    lastMs = nowMs;

    const whole = Math.floor(head);
    if (whole > lastWholeRecord) {
      pendingAppends += Math.min(whole - lastWholeRecord, 8);
      lastWholeRecord = whole;
    }

    readers[0].pos = head - 1.4 - 0.35 * Math.sin(t * 0.7); // caught up
    readers[1].pos = head - (7 + 5.5 * Math.sin(t * 0.17)); // falls behind, drains
    readers[2].pos = replayPos; // replaying from an earlier position
  }

  return {
    advance,
    readers,

    get head() {
      return head;
    },
    get slowest() {
      return Math.min(readers[0].pos, readers[1].pos, readers[2].pos);
    },
    /** Records the slowest reader has not reached. The lag, in records. */
    get backlog() {
      return head - Math.min(readers[0].pos, readers[1].pos, readers[2].pos);
    },

    /** 1 at the instant record `n` lands, decaying toward 0 behind the head. */
    recency(n) {
      return Math.exp(-(head - n) / RECENCY_TAU);
    },
    /** True while record `n` is still work for at least one reader. */
    pending(n) {
      return n > Math.min(readers[0].pos, readers[1].pos, readers[2].pos);
    },
    isIndex(n) {
      return n % INDEX_EVERY === 0;
    },

    /** Whole appends since the last drain. Discrete fields fire on this. */
    drainAppends() {
      const n = pendingAppends;
      pendingAppends = 0;
      return n;
    },

    setReduced(value) {
      reduced = Boolean(value);
    },
  };
}
