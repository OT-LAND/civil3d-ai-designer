# Roadmap

Status keys: `todo` · `doing` · `review` (PR open) · `test` (needs owner's Civil 3D test) · `done` · `blocked:D#`

**Priority from 2026-10-25 (D14):** finish open PRs (#6–#10), then **MB** top to bottom, then
1.3, 2.3, M4. Concept: `docs/CONCEPT.md`.

## MB — Design Brief & semi-parametric palette (D14, concept v1)
| # | Item | Status |
|---|---|---|
| B.1 | Design Brief schema v1 in Core: background refs, zones + character sliders (global, optional per-zone override), tagged curves (walkingNetwork/contour/flowBasin/border), tagged points (entrance/interestPoint/elevation/tree), style (images + prompt); JSON read/write, validator, sample, tests | todo |
| B.2 | Metrics on a Design Intent (Core): hard/soft share, canopy cover, curved/straight share, accessible share, steepest slopes; compare to slider targets with tolerance → "target vs achieved" | todo |
| B.3 | Design Intent v1: zone refs, trees (new + kept), drainage (swale, basin, low points), site elements, interest-point places | todo |
| B.4 | Style profile: reference images + prompt → material palette, planting character, form language (Claude vision), cached | todo |
| B.5 | Plan stage: brief + style profile + site → Design Intent spaces/paths; repair loop on metric misses and rule failures (extends 1.2); variants per D18 | todo — variants blocked:D18 |
| B.6 | Grading solver v0 (Core): levels from entrances/FFL, elevation points, contours, survey; max grade, cut/fill and max slope targets; reasons sent back to Plan | blocked:D16,D17 |
| B.7 | Civil 3D palette, five panels per sketch: pick & tag objects (tags stored on the objects), tag colours, sliders, style image slots, background assignment → Design Brief | todo — after 0.3 |
| B.8 | Generate → variants → target-vs-achieved card + rule report → Apply in one undo (replaces 1.4) | todo — after B.5, B.7 |

## M0 — Foundations
| # | Item | Status |
|---|---|---|
| 0.1 | Solution skeleton: `Core` (no Autodesk refs), `Civil3DPlugin`, `Tests`; Core builds and tests on Linux | done |
| 0.2 | GitHub Actions: build + test on every PR | done |
| 0.3 | Plugin load (bundle/PackageContents.xml), ribbon tab, empty palette, `AIHELLO` command (owner tests), Civil 3D 2027 | todo |
| 0.4 | Claude API client in `Core` (key from settings, retries, image input, structured JSON output) | done |

## M1 — Design Intent pipeline (text → graded spaces)
| # | Item | Status |
|---|---|---|
| 1.1 | Design Intent JSON schema v0: site, spaces (plaza/path/lawn/bed), levels, edges, materials | done |
| 1.2 | Prompt + validator: free text → Design Intent | done |
| 1.3 | Builders: existing surface from survey; feature lines + proposed surface for spaces | todo |
| 1.4 | Preview-and-confirm UI before writing to the drawing | superseded by B.8 |

## M2 — Background data & constraints
| # | Item | Status |
|---|---|---|
| 2.1 | Import survey, existing trees, lot borders, buildings/FFLs (DWG survey first; trees as DWG blocks or separate table with field mapping; read units/coordinate system or ask, per D4b) | todo |
| 2.2 | Rule engine: accessible route slopes, ramp/step geometry, max lawn/bed slopes, drainage falls, tree protection zones, setbacks; rules from a selectable rule-set file, default set = published Israeli accessibility values (D3) | todo |
| 2.1c | Tree table: read .xlsx directly, same field mapping as CSV; trunk = diameter (D12) | todo — after PR #8 |
| 2.2c | Rule set: mark D10 values verified; Design Intent path flag "not accessible", skipped by accessible-route rules (D11) | todo — after PR #7 |
| 2.3 | Constraint report palette (pass/fail per rule, zoom to problem) | todo |

## M3 — Visual inputs
| # | Item | Status |
|---|---|---|
| 3.1 | Hand sketch over site plan → spaces and edges, with scale/georeference calibration | superseded by tagged geometry (D14); maybe later as "trace scan into tagged geometry" |
| 3.2 | Concept diagram → zoning, circulation and level intent | superseded by tagged geometry (D14) |
| 3.3 | Render/reference image → material palette and planting character | moved to B.4 |
| 3.4 | Multi-input fusion: text + sketches + images + background into one Design Intent | superseded by Design Brief (B.1, B.5) |

## M4 — Detailed landscape design
| # | Item | Status |
|---|---|---|
| 4.1 | Grading detail: tie-ins to FFLs and existing ground, berms, swales, cut/fill | todo |
| 4.2 | Hardscape: steps, ramps with landings, retaining/seat walls with top/bottom levels, paving zones | todo |
| 4.3 | Softscape: planting beds, lawn, tree placement; AI proposes species from the user-selected/uploaded plant list (D5); existing trees retained | todo |
| 4.4 | Drainage: flow arrows, inlets, simple storm pipes, infiltration areas | todo |
| 4.5 | Site elements: furniture, lighting, shade, play as attributed blocks | todo |
| 4.6 | Documentation: level/slope labels, plant schedule, material and quantity take-offs | todo |

## M5 — Designer UX
| # | Item | Status |
|---|---|---|
| 5.1 | Template manager: user chooses/uploads .dwt, layers, styles, label sets, material/plant/furniture libraries, rule sets; ships a default starter template, rule set and plant list (D3, D5) | todo |
| 5.2 | Post-generation editing by instruction ("lower the plaza 15 cm, ramp at 1:15") | todo |
| 5.3 | Generation history: compare versions, roll back | todo |
| 5.4 | MCP connector: expose Core tools so Claude (on the designer's own account) can drive the design agentically; user picks palette or connector mode (D9) | todo — after Core loop |
