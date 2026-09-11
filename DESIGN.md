---
name: Kurrent Surge Docs
description: Editorial documentation set on a raised blue-black ground, lit by one azure hue split across three stops, with a warm gold held back for interaction.
colors:
  ground: "#0f181f"
  ground-lifted: "#19262f"
  ground-raised: "#24323c"
  frost: "#e9f4fc"
  frost-quiet: "#b5c7d5"
  frost-muted: "#819cb0"
  frost-faint: "#7690a4"
  frost-ghost: "#6e8597"
  accent: "#60c2ff"
  accent-fill: "#0077d9"
  accent-deep: "#005fbd"
  lit: "#f2cf3b"
  outcome: "#61cb7c"
  lit-light: "#8c6a00"
  outcome-light: "#00822b"
  cta-fg: "#ffffff"
  code-ground: "#09141c"
  code-fg: "#b5c7d5"
  daylight: "#f9fbfe"
  daylight-raised: "#ffffff"
  daylight-recessed: "#ecf3f8"
  slate: "#0d151a"
  slate-quiet: "#354550"
  slate-muted: "#566f82"
  slate-faint: "#5b7485"
  slate-ghost: "#5f7585"
  accent-ink: "#006fce"
  code-ground-light: "#edf2f8"
  code-fg-light: "#354550"
  aside-note: "#c099ff"
  aside-tip: "#61cb7c"
  aside-caution: "#e7a226"
  aside-danger: "#ff8880"
  aside-note-light: "#7a4aba"
  aside-tip-light: "#00822b"
  aside-caution-light: "#9e5400"
  aside-danger-light: "#b63132"
  ink: "#1a1a1a"
  ink-dim: "#525252"
typography:
  display:
    fontFamily: "Poppins, sans-serif"
    fontSize: "48px"
    fontWeight: 300
    lineHeight: 1.15
    letterSpacing: "-0.02em"
  headline:
    fontFamily: "Poppins, sans-serif"
    fontSize: "28px"
    fontWeight: 400
    lineHeight: 1.25
    letterSpacing: "-0.01em"
  title:
    fontFamily: "Poppins, sans-serif"
    fontSize: "20px"
    fontWeight: 500
    lineHeight: 1.35
    letterSpacing: "-0.008em"
  body:
    fontFamily: "Poppins, sans-serif"
    fontSize: "17px"
    fontWeight: 300
    lineHeight: 1.7
  label:
    fontFamily: "Poppins, sans-serif"
    fontSize: "11px"
    fontWeight: 600
    letterSpacing: "0.08em"
  code:
    fontFamily: "Fira Code, monospace"
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.7
rounded:
  hairline: "2px"
  chip: "4px"
  control: "6px"
  surface: "8px"
  full: "999px"
spacing:
  hair: "4px"
  tight: "8px"
  snug: "12px"
  base: "16px"
  cell: "20px"
  block: "24px"
  gutter: "32px"
  section: "36px"
  major: "48px"
components:
  button-primary:
    backgroundColor: "{colors.accent-fill}"
    textColor: "{colors.cta-fg}"
    rounded: "{rounded.chip}"
    padding: "16px 22px"
    typography: "{typography.body}"
  button-primary-hover:
    backgroundColor: "{colors.accent-fill}"
    textColor: "{colors.cta-fg}"
  button-secondary:
    backgroundColor: "transparent"
    textColor: "{colors.frost-quiet}"
    rounded: "{rounded.chip}"
    padding: "15px 18px"
  button-tertiary:
    backgroundColor: "transparent"
    textColor: "{colors.frost-muted}"
    rounded: "0"
    padding: "14px 4px"
  sidebar-link:
    backgroundColor: "transparent"
    textColor: "{colors.frost-muted}"
    rounded: "{rounded.control}"
    padding: "6px 12px"
  sidebar-link-hover:
    textColor: "{colors.frost}"
  sidebar-link-active:
    textColor: "{colors.accent}"
    rounded: "0"
  toc-link:
    textColor: "{colors.frost-ghost}"
    padding: "4px 0"
  toc-link-active:
    textColor: "{colors.accent}"
  search-trigger:
    textColor: "{colors.frost-ghost}"
    rounded: "{rounded.surface}"
    typography: "{typography.body}"
  inline-code:
    textColor: "{colors.frost-quiet}"
    rounded: "{rounded.chip}"
    padding: "0.12em 0.42em"
  code-frame:
    backgroundColor: "{colors.code-ground}"
    textColor: "{colors.code-fg}"
    rounded: "{rounded.surface}"
  aside-note:
    textColor: "{colors.aside-note}"
    rounded: "{rounded.surface}"
    padding: "20px 24px"
  aside-tip:
    textColor: "{colors.aside-tip}"
    rounded: "{rounded.surface}"
    padding: "20px 24px"
  aside-caution:
    textColor: "{colors.aside-caution}"
    rounded: "{rounded.surface}"
    padding: "20px 24px"
  aside-danger:
    textColor: "{colors.aside-danger}"
    rounded: "{rounded.surface}"
    padding: "20px 24px"
  table-head:
    backgroundColor: "{colors.ground-lifted}"
    textColor: "{colors.frost}"
    padding: "10px 14px"
  pagination-card:
    backgroundColor: "{colors.ground-lifted}"
    textColor: "{colors.accent}"
  pagination-card-hover:
    backgroundColor: "{colors.ground-raised}"
  footer-control:
    backgroundColor: "{colors.ground-lifted}"
    textColor: "{colors.frost}"
    rounded: "{rounded.surface}"
    height: "42.5px"
    padding: "0 11.9px"
  toggle-cell:
    backgroundColor: "transparent"
    textColor: "{colors.frost-ghost}"
    rounded: "{rounded.chip}"
    height: "30.6px"
    padding: "0 8.5px"
  toggle-cell-active:
    textColor: "{colors.frost}"
  select-item:
    backgroundColor: "transparent"
    textColor: "{colors.frost}"
    rounded: "{rounded.control}"
    padding: "9.35px 11.9px"
  sidebar-toggle:
    backgroundColor: "{colors.ground}"
    textColor: "{colors.frost-ghost}"
    size: "27.6px"
---

# Design System: Kurrent Surge Docs

## Overview

**Creative North Star: "The Lit Page"**

An editorial page printed on blue-black stock, with one light source above it. Everything in this
system follows from that image. Type does the work: the largest text carries the least weight,
and each step down gains weight as it loses size, so hierarchy is read from proportion rather than
from boxes, rules, or colour. Colour is rationed to a single azure family, and every emphasis in
the interface is a percentage mix of it against transparency. Nothing is painted with an unrelated
hue, so the whole page repaints from one small set of tokens.

The ground is deliberately not black. It sits at OKLCH lightness 20.4%, well off the floor of the
range, because a cast shadow needs something beneath it to darken. At the bottom of the range
nothing can be darker than the page and every elevation cue disappears at once. Raising the ground
is what lets the system use real shadows, and the neutral ramps above it are cool throughout —
every step keeps blue at or above red, because beside an azure accent a warm grey reads
yellow-brown.

The accent is split rather than shaded. No single value can be both a legible link and a saturated
fill on this ground, so the family carries three stops: a bright one for text, a saturated one for
fills that must hold white, and a deeper one for pressed states. The register is restrained,
editorial, and engineered. Confidence shows in the omissions that were made deliberately and
recorded — the film grain was removed so dark fields stay flat, and the rule under the page title
was removed because it read as a divider between a heading and its own first paragraph.

**Key Characteristics:**

- One azure family, three stops, split by role rather than by shade
- Inverse weight hierarchy — 300 at 48px, 500 at 20px
- A raised ground, so shadow has somewhere to fall
- Cool neutrals throughout; every step of both ramps clears 4.5:1
- Flat fields, hairline borders, no boxes where a rule will do
- One fixed radial wash as the sole ambient gradient
- 17px root, so every `rem` in the system is 17-based
- Dark-first: bare `:root` is dark, light is the override

## Colors

Two complete palettes drawn from the same azure family, over cool neutral ramps in both directions.

### Primary

- **Signal Azure** (`#60c2ff`, `oklch(78.0% 0.127 239)`): the text stop. Prose links, the active
  sidebar rail and its wash, the current table-of-contents entry, focus outlines, and the ambient
  wash's hue. It reaches 9.10:1 on the ground. It is never a fill — white on it measures 1.97:1.
- **Azure Fill** (`#0077d9`, `oklch(56.9% 0.174 253)`): the fill stop. Primary buttons, text
  selection, the sidebar current-page background, and the base the hover ring is mixed from. It
  carries white at 4.53:1.
- **Azure Deep** (`#005fbd`, `oklch(49.5% 0.166 256)`): pressed and hover states on a filled
  surface. White at 6.23:1.
- **Azure Ink** (`#006fce`, `oklch(54.3% 0.169 253)`): the light theme's text stop, at 4.86:1 on
  its ground. The fill and deep stops are shared unchanged across both themes.

### Secondary — chrome

Two values that carry meaning the azure family cannot, and that never appear in prose.

- **Lit Gold** (`#f2cf3b`, `oklch(86.0% 0.160 95)` dark; `#8c6a00`, `oklch(54.3% 0.111 86)` light):
  the interaction hue. Hover and focus, and nothing at rest. 11.74:1 on the dark ground, 4.83:1 on
  the light one. It is a text colour and never a fill. The light stop is measured from its hex
  rather than derived from the dark one: 0.160 chroma leaves sRGB at that lightness, so the shipped
  value is the clipped result, not the authored one.
- **Outcome** (`#61cb7c`, `oklch(75.9% 0.150 150)` dark; `#00822b`, `oklch(52.8% 0.159 147)` light):
  the payoff line on a capability card and the confirming tick beside a claim. It opens on the tip
  callout's value and is free to move away from it.

Gold completes the North Star rather than diluting it. The system's image is a page under one
light source, but until now that light was cold, and every real light source is warm. Azure is the
ink; gold is the lamp.

### Neutral — dark scope

- **Ground** (`#0f181f`), **Ground Lifted** (`#19262f`), and **Ground Raised** (`#24323c`): the
  three surfaces, at roughly even five-point lightness steps in the 240° hue band.
- **Frost** (`#e9f4fc`): headings and highest-contrast text, at 16.07:1.
- **Frost Quiet** (`#b5c7d5`): body copy, 10.33:1.
- **Frost Muted** (`#819cb0`), **Frost Faint** (`#7690a4`), **Frost Ghost** (`#6e8597`): the
  descending ramp for secondary labels, resting navigation, and the table-of-contents rail. The
  lowest step still reaches 4.67:1, so no role in the system sits below AA body text.
- **Code Ground** (`#09141c`) with **Code Foreground** (`#b5c7d5`): the only surface that goes
  darker than the page. Narrower in reach than the name suggests — Expressive Code frames, which
  are almost all the code on the site, paint `--bg-1` instead. This token is consumed by bare
  `<pre>` blocks, of which the landing's install line is the only live instance.

### Neutral — light scope

- **Daylight** (`#f9fbfe`): the page. **Daylight Raised** (`#ffffff`): lifted surfaces, which go
  *lighter* than the page rather than darker. **Daylight Recessed** (`#ecf3f8`): the sunken step.
- **Slate** (`#0d151a`), **Slate Quiet** (`#354550`), **Slate Muted** (`#566f82`),
  **Slate Faint** (`#5b7485`), **Slate Ghost** (`#5f7585`): the foreground ramp, lowest step at
  4.64:1.
- **Code Ground Light** (`#edf2f8`) with **Code Foreground Light** (`#354550`).

### Tertiary — callouts

Four roles, two themes, eight values. Dark: **Note** `#c099ff`, **Tip** `#61cb7c`,
**Caution** `#e7a226`, **Danger** `#ff8880`. Light: `#7a4aba`, `#00822b`, `#9e5400`, `#b63132`.
Each is applied through a single `--aside` custom property at 6% fill and 14% border, so icon,
title, and border move together. A prose link inside a callout takes the callout's hue rather than
the accent — verified in the render, where the same page shows a link at `#60c2ff` in body copy and
at `#c099ff` inside a note.

### Diagram ink

**Ink** (`#1a1a1a`) and **Ink Dim** (`#525252`) sit outside both foreground ramps because D2
diagrams keep a white canvas under either theme, so their text needs a colour that is not tied to
the page ground. They are the only surviving members of an abandoned light-band experiment; the
rest of that group was removed.

Every token in this system has a consumer. Nothing is held in reserve.

### Named Rules

**The Text/Fill Split Rule.** `--accent` is a text colour and never a fill. `--accent-fill` is a
fill and never text. The split is not cosmetic: on this ground the bright stop reads text at
9.10:1 but would put white at 1.97:1, and the fill stop carries white at 4.53:1 but would read as
text at only 3.96:1. Reaching for the wrong stop breaks contrast in one direction or the other,
every time.

**The Mix, Never Match Rule.** Every accent emphasis is written
`color-mix(in oklab, var(--accent…) N%, transparent)`. No emphasis colour is ever spelled as a
literal. Text emphases mix from `--accent`; the hover ring mixes from `--accent-fill`.

**The One Family Rule.** All accent stops sit in one narrow hue band — measured at 239°, 253°, and
256°. There is no secondary *brand* colour: identity is carried by azure alone. When two things
must be told apart within the brand, they are told apart by weight, size, position, or mix
percentage. Gold and outcome are not exceptions to this, because neither states identity — see the
Chrome-Only Rule.

**The Chrome-Only Rule.** `--lit` and `--outcome` live in chrome and never in prose. The hue wheel
is full: five committed hues each wanting 60° of separation is 600° of demand on a 360° wheel, and
the widest gap available gives 49°. So a new value cannot clear every neighbour, and the question
becomes which collision can never occur. `--lit` sits 19° off the caution callout, and that is safe
only because caution appears inside callouts and `--lit` only in chrome — the two never share a
surface. Take either into a paragraph and the separation stops holding.

**The Own-Your-Value Rule.** A hue that sits near a callout must still be its own declaration.
`--outcome` opens on the tip callout's exact value, but as a separate token, because the four aside
hues are matched in lightness within 1.07 of each other on the ground: brightening a shared value
for a landing page would drag the tip callout out of that set, to 13.17:1 against danger's 7.76:1.
Two owners on one declaration means neither can be tuned.

**The Isoluminant Asides Rule.** The four callout hues are matched in lightness and chroma and
separated by hue alone — dark at L 76% and C 0.15 across hues 25 / 76 / 150 / 301, light at L 52%
across hues 25 / 58 / 147 / 300. Each sits at least 60° off the accent band, so no callout title
can be mistaken for a link, and each is far enough from the others to be told apart at a glance.
They are not brand tokens and must not be promoted into the palette.

## Typography

**Display Font:** Poppins (300 / 400 / 500 / 600 / 700, with true italics)
**Body Font:** Poppins
**Label Font:** Poppins
**Code Font:** Fira Code (variable, 300–700)
**Reader alternative:** OpenDyslexic (400 / 700), selectable from the footer

**Character:** A geometric sans set unusually light, carrying every register on the page including
the uppercase index labels. The monospace appears only where characters must line up.

### Hierarchy

- **Display** (300, 48px, 1.15, -0.02em): page titles. On the landing, `clamp(52px, 10vw, 112px)`
  at 300 weight for the product name.
- **Headline** (400, 28px, 1.25, -0.01em): section headings. 48px of air above, 16px below.
- **Title** (500, 20px, 1.35, -0.008em): subsections. 36px above, 12px below.
- **Body** (300, 17px, 1.7): prose. The reading column is 55rem (935px). Markdown containers sit
  at 450 weight; paragraphs and list items drop to 300.
- **Label** (600, 11px, 0.08em, uppercase): sidebar group labels and the table-of-contents heading.
  The landing runs two looser variants at weight 500 — 12px at 0.13em for the eyebrow and 12.5px at
  0.14em for section labels.
- **Code** (Fira Code, 14px, 1.7): code frames. Inline code sits at 15px inside 17px prose.

### Named Rules

**The Inverse Weight Rule.** The largest type carries the least weight. Size falls and weight
rises: 300 at 48px, 400 at 28px, 500 at 20px. This is the single most identifying property of the
system, and it is why headings need no colour or rule to separate them.

**The Mono Is For Code Only Rule.** Fira Code sets code and nothing else. Uppercase index labels
take the sans, because a monospace fixes every glyph to one advance, and the wide tracking an
uppercase label wants then pushes the letters past the point where they read as words at all.

**The Reader Owns The Face Rule.** OpenDyslexic is a first-class option, not an afterthought. It
runs 41% wider than Poppins at matched x-height, so its scope sets `font-size-adjust: 0.397` — the
measured width match — and the switch reflows no line. Code is exempt from the adjustment.

## Layout

The reading column is governed by one lever. `--sl-content-width` is 55rem (935px) and no CSS
`max-width` competes with it; `.main-pane` zeroes `--sl-content-margin-inline` so the column sits
beside the navigation instead of being pushed to the right edge. The landing page uses its own
containers: 1080px standard, 880px tight, both with a 32px gutter.

Vertical rhythm is asymmetric by design — more space above a heading than below it. Headings take
48/16 and 36/12. Callouts take 24 above and 28 below. Tables take 16 and 24. Table cells are
10px by 14px; sidebar rows are 6px by 12px.

Root font-size is 17px, which scales the whole type system from one lever because every Starlight
size token is expressed in `rem`.

The sidebar collapses on a 250ms width transition above 800px, driven by `data-sidebar-collapsed`
on the root element and restored before first paint. Only `--sl-content-inline-start` goes to zero;
`--sl-sidebar-width` is left alone because Starlight also sizes the right-hand rail from it.

The footer uses container queries rather than viewport queries, reflowing its preference rail from
one column to two at 306px and to three at 442px of its own inline size.

### Named Rules

**The Two Rem Bases Rule.** Root font-size is 17px, so `1rem` is 17px in every declaration — but
`@media` resolves `rem` against the initial 16px while `@container` resolves it against the
computed 17px. `@media (min-width: 50rem)` is 800px; `@container (min-width: 18rem)` is 306px.
Never carry a breakpoint figure from one context to the other.

## Elevation & Depth

Surfaces are flat. There is no gradient on any panel, card, button, or field. Depth comes from
three devices: a hairline border, a step on the surface ramp, and a shadow that now works on both
themes because the ground was raised to make room for it.

The system uses shadows and it uses one gradient per page, both specified rather than scattered. On
every documentation page the gradient is an accent-tinted radial, fixed to the top of the viewport,
sized 100% by 60vh, at 12% strength on dark and 3% on light. It is painted as a background layer on
`body` rather than a pseudo-element, so it composites under every descendant, creates no stacking
context, and cannot intercept a pointer event.

The landing page reaches the same effect by a different route. `home.css` sets `body { background:
var(--bg) }`, and that shorthand resets the `background-image` the docs theme declares, so the body
wash is `none` there. The landing supplies its own through `.hero::before` at 22% — stronger,
because it is scoped to the hero rather than to the whole page.

### Shadow Vocabulary

- **Lift, dark** (`inset 0 1px 0 rgba(190,220,250,0.10), 0 1px 2px rgba(0,6,12,0.45)`): raised
  surfaces. The inset edge is where a lit surface would catch the light; the shadow beneath now
  reads, because the ground is off the floor.
- **Lift, light** (`0 1px 2px rgba(13,21,26,0.07), 0 6px 20px rgba(13,21,26,0.06)`): the same
  surfaces under the light theme, with `--lift-edge` set to `transparent`.
- **Code frame** (`inset 0 1px 0 rgba(245,243,240,0.06), 0 2px 10px rgba(0,0,0,0.55)`): a deeper
  fall for blocks that cross the ambient wash.
- **Popup** (`0 1rem 2.5rem black/32%, inset 0 1px 0 white/7%`): the only genuinely floating
  surface in the system, 17px down with a 42.5px blur.
- **Focus ring** (`0 0 0 4px var(--accent-ring)`): a 30% mix of `--accent-fill` in the dark theme,
  26% in the light, on primary-button hover.

### Named Rules

**The Raised Floor Rule.** The dark ground sits at OKLCH lightness 20.4%, deliberately off the
bottom of the range, because a cast shadow needs something beneath it to darken. Never push the
ground toward black to gain contrast: it removes every elevation cue in the system at once.

**The Edge Inverts Rule.** Elevation reverses with the theme. A raised dark surface is lighter than
its ground and catches a highlight along its top edge, so the dark theme uses the inset edge *and*
the shadow. A raised light surface cannot get meaningfully lighter than an already-near-white page,
so the light theme drops the edge to `transparent` and the shadow carries alone.

**The One Wash Rule.** A page carries exactly one gradient and it belongs to the page or its hero,
never to a component. Its strength is theme-specific and not symmetric: 12% on dark, 3% on light,
because an accent wash over a near-white ground reads as a smudge rather than as light. The landing
runs its own at 22% because that one is scoped to a hero, not to a whole page.

## Shapes

Rectangular, hairline-ruled, softly cornered. The radius scale has five steps and no others: 2px
for text highlights, 4px for chips and buttons, 6px for control rows and markers, 8px for every
enclosing surface, and a full pill for switch tracks and the selected-item rail. 8px is the
dominant value. Expressive Code is configured at 7px because it composes its outer radius as
`borderRadius + borderWidth`, which lands the rendered frame on 8px with the rest.

Borders are the primary structural device. Structural borders are 1px with three exceptions. 2px
carries focus outlines, the select popup's selected-item rail, and the hero paragraph's accent
left border. 3px appears exactly twice: the active sidebar rail, and the divider that the header's
KurrentDB link carries as its own leading border.

Two shapes are deliberately asymmetric. The active sidebar row drops to a 0px radius, because the
6px hover-pill radius also rounded the 3px marker and made it read as a curve rather than a rule.
The sidebar toggle is a half-pill at `0 6px 6px 0` with its inline-start border removed, so it
reads as extruded from the pane seam rather than floating over it. It has to extrude into the prose
gutter and sit level with the content: `.sidebar-pane` paints over anything left of the seam at any
z-index, and the header band paints over anything level with itself, so both alternatives make the
button disappear entirely. At 27.6px it reaches 2px into the reading column; at its original 34px
it reached 9px and the page title, which scrolls under this fixed button, collided with it.

### Named Rules

**The Hairline Rule.** Every structural border is 1px. Reach for 2px only for a focus outline or a
selection marker, and for 3px only where an existing 3px marker already establishes the weight.

**The Lattice, Not Cards Rule.** Where a grid of related items is needed, draw single shared 1px
borders conditionally per index at zero gap and zero radius, so the group reads as one ruled table.
The landing's six-cell feature grid is the reference implementation. Do not turn it into cards.

## Components

### Buttons

- **Shape:** softly cornered (4px), 1px border matching the fill.
- **Primary:** `--accent-fill` with white text, 17px at weight 500, 16px by 22px padding.
- **Hover / Focus:** a 4px ring at 30% `--accent-fill`; `translateY(1px)` on active. Transitions
  are 120ms on transform and 160ms on the ring.
- **Secondary:** transparent fill, 1px hairline border, quiet-ramp text. Hover lifts the text to
  full contrast and the border one step.
- **Tertiary:** no border box at all — a dotted 1px underline and a mono label at 13px, with the
  trailing arrow translating 2px on hover.

### Navigation — sidebar

- **Resting:** 14px at weight 400, muted text, a 6px pill, and a 3px transparent left border held
  permanently in reserve so nothing shifts on state change.
- **Hover:** a faint tint fill and full-contrast text.
- **Active:** `--accent` text, an 8% accent wash, the reserved left border filled with the accent,
  and the radius dropped to 0.
- **Group labels:** sans, 11px, 600, 0.08em, uppercase.
- **Repository link:** pinned to the bottom of the pane by a growing flex spacer, with radius and
  left border explicitly reset and the hover background explicitly held transparent against
  Starlight's own fill.

### Navigation — table of contents

Deliberately quieter than the sidebar and deliberately different: active entries take accent text
and no rail, because the rail is the left navigation's device and repeating it on the right
competes with the prose. 13px at weight 400 on the ghost step.

### Inputs / Fields

- **Style:** a filled, rounded field rather than a hairline chip — faint tint background, 1px tint
  border, 8px radius.
- **Focus:** the control suppresses its own outline and the *wrapper* takes the signal, shifting
  its border to a 40% accent mix and lifting its icon from the ghost step to full contrast.
- **Search trigger** uses the same recipe, with the keyboard hint rendered transparent in the ghost
  step.

### Cards / Containers

- **Corner Style:** 8px, the single enclosing-surface radius.
- **Background:** the lifted step of the surface ramp.
- **Shadow Strategy:** see Elevation & Depth — the pair on dark, the shadow alone on light.
- **Border:** 1px, on the container rather than the content, so rounded corners hold.
- **Internal Padding:** 20px by 24px for callouts, 10px by 14px for table cells.

### Callouts

A tinted, rounded card keyed by kind rather than Starlight's default left-bar note. Each kind sets
a single `--aside` custom property, and one shared rule reads it for the title colour, a 6% fill,
and a 14% border, so icon, title, and border stay in step. Titles run 15px at 600; content drops to
15px at 1.65 line-height.

### Code frames

Code surfaces follow the theme. This is load-bearing rather than cosmetic: Expressive Code swaps
between the `starlight-dark` and `starlight-light` syntax themes, and the light theme's token
colours are dark, so pinning the surface dark makes part of the syntax vanish into its own
background.

Two surfaces carry code, and they are not the same token. Expressive Code frames — configured in
`ec.config.mjs`, and the form nearly all code on the site takes — paint `--bg-1`, measured at
`#19262f` on dark and `#ffffff` on light. Bare `<pre>` blocks paint the
`--code-bg` / `--code-fg` / `--code-border` trio at a 10px radius. Both follow the theme; only the
second is named for it.

### Tables

The border lives on the container so the radius holds; the head is a faint wash rather than a rule;
the last row drops its bottom border. Markdown tables are forced back to `display: table` at full
width, because Starlight sets them to `display: block; overflow: auto`, which makes the element a
block box that carries the border at full width while the anonymous table box inside shrinks to its
content.

### Segmented Control (signature)

The footer's theme selector. A 42.5px track at 8px radius on a faint tint, with an absolutely
positioned indicator that slides on `transform 180ms ease`, driven by two custom properties
(`--cells` and `--index`) written before hydration from local storage. It carries a 1px `--accent`
border over a 24% `--accent` fill: markers take the bright stop, because nothing sits on top of
them — the same reasoning that puts the select popup's 2px selected rail on `--accent` rather than
`--accent-fill`. The cells themselves never change background; selection reads as a three-step
colour ladder on one property only — ghost at rest, quiet on hover, full contrast when on. The
individual
cell suppresses its own outline and the track rings for it, using `:has(:focus-visible)` rather
than `:focus-within` so a mouse click does not leave it ringed.

### Deep Arc (signature)

The landing page's one authored moment, and the only animation on it. A Three.js field fixed to the
viewport, behind every section: lightning falls from above the top edge, one or two channels struck
two or three times in quick succession, crawling the letterforms of "Surge" when it reaches them and
breaking away downward into depth. Between strikes the field is empty. The silence is the effect —
a bolt every five or six seconds, never a hum.

It renders the product's own semantics rather than decorating them. A strike is an append. The three
readers are the points a channel grounds on, set at increasing depth, so a lagging reader is
literally further away. The backlog is carried by four channels at once — how far a strike reaches,
how many are alive, how long they persist, and how often they fire — because nothing between strikes
is drawn at all. The replaying reader is the single element in `--accent-fill`.

Bolts are held at full strength for 260ms before quenching. Without that hold a segment decays from
the instant it is drawn, only the leading few are ever bright, and the eye sees a travelling dot on
a dim line instead of a discharge.

Two deliberate deviations from this system, each isolated to one constant and each exposed as a
slider in the motion lab:

- **Alpha.** A live bolt runs at `0.78`, above the `0.45` per-element ceiling. A discharge at the
  same alpha as a resting line is not a discharge. Afterglow and every quiet state still obey `0.45`.
- **Glow.** A halo, which this system otherwise bans outright. It is two extra 1px copies of the
  channel offset perpendicular to it in screen space — not a wider line, which would need capped
  quads and put a visible disc at every kink. It lights live segments only, never the afterimage.

Reach is a mask the host applies, graded down the document: full through the hero, gone within one
and a half screens. The field pauses off-screen, on a hidden tab, and renders one frozen strike for a
reader who asked for reduced motion. A machine with no WebGL context gets no field and no error; the
hero is designed to stand without it.

Its predecessor was a Canvas 2D live-log strip in the hero, drawing the same
model on one baseline. It was deleted when this field replaced it; `log-model.js`
is what survives of it.

### Preference Cell (signature)

Three different controls — a segmented group, a switch, and a select — share one geometry contract
so the rail reads as a single instrument: 42.5px height, 1px hairline border, 8px radius, faint
tint surface. That triple is the footer's signature, and any control added there must adopt it.

## Do's and Don'ts

### Do:

- **Do** use `--accent` for text and `--accent-fill` for fills, every time. `--accent-deep` is the
  pressed state on a filled surface.
- **Do** express every accent emphasis as `color-mix(in oklab, var(--accent…) N%, transparent)`, so
  a hue change stays a token edit.
- **Do** keep the dark ground off the floor of the lightness range. Shadow depends on it.
- **Do** pair the inset light edge with the shadow on dark, and drop the edge on light.
- **Do** keep the ambient wash accent-tinted and anchored to `body`, and keep its light-theme
  strength far below its dark-theme strength.
- **Do** let size fall as weight rises. A heading is established by proportion, not by a rule or a
  colour.
- **Do** give a focus ring a 2px accent outline with a 2px offset, and move the signal to the
  wrapper when the control itself must suppress its outline.
- **Do** reserve borders in a transparent state when a state change would otherwise shift layout —
  the sidebar row and the social icon both do this.
- **Do** hold every colour in `surge.css`. D2 diagrams, code frames, and Starwind components all
  resolve from it, and a colour written anywhere else stops following the theme toggle.
- **Do** adopt the 42.5px / 1px / 8px / faint-tint contract for any new control added to the footer
  rail.

### Don't:

- **Don't** put `--accent` under white text or on a fill, and don't use `--accent-fill` as text.
  Each fails contrast in the opposite direction.
- **Don't** introduce a hue outside the azure band for anything structural. Distinguish by weight,
  size, position, or mix percentage. `--lit` and `--outcome` are not structural: one marks the
  pointer, the other a claim, and both stay out of prose.
- **Don't** take `--lit` or `--outcome` into a paragraph, a heading, or a callout. Their separation
  from the aside hues holds only because they never share a surface with them.
- **Don't** point `--outcome` at the tip callout's variable, or the reverse. They may hold the same
  value; they may never be the same declaration.
- **Don't** use the brand accent to mean "information", and don't spend `--lit` on anything at rest.
- **Don't** pin a code surface to one theme. The syntax themes swap, and a pinned surface hides
  tokens that share its colour.
- **Don't** put a gradient on a panel, card, button, or field. The system's one gradient belongs to
  the page.
- **Don't** set a structural border above 1px. 2px is for focus and selection markers, and 3px only
  where a 3px marker already exists.
- **Don't** write a colour as a literal hex outside the token block.
- **Don't** carry a `rem` breakpoint figure between `@media` and `@container`. They resolve against
  different bases here.
- **Don't** add a `max-width` that competes with `--sl-content-width`. It is the only lever on the
  reading column.
- **Don't** ship a new interactive element with no focus style. Every control this project authors
  now rings at 2px `--accent` — 9.10:1 against the dark ground — with a 2px offset, or `-2px` where
  the element spans the full width of its container and a positive offset would draw off the edge.
  One element suppresses its own ring on purpose: the sidebar filter input, whose wrapper carries
  the signal instead. Starlight's own chrome, such as the skip link, keeps the browser default;
  that surface belongs to the framework, not to this system.
- **Don't** add a step to the radius scale, and don't express one in `rem`. Every radius in the
  system is a literal pixel value drawn from 2 / 4 / 6 / 8 / 999. `rem` at a 17px root produced
  8.5px and 6.8px values that read as a fourth and fifth step; they are gone.
