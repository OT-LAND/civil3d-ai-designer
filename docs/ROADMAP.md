# Roadmap

Status keys: `todo` · `doing` · `review` (PR open) · `test` (needs owner's Civil 3D test) · `done` · `blocked:D#`

**Priority from 2026-10-25 (D14):** Core-only stacks #7→#9 and #8→#10 merged 2026-10-25 (D13); #6 tested OK except bug 0.3a (fix first), then **MB** top to bottom, then
1.3, 2.3, M4. Concept: `docs/CONCEPT.md`.

## Bugs (top priority)
| # | Item | Status |
|---|---|---|
| 0.3a | **Palette text invisible** (owner test 2026-10-26): palette background is black in the dark theme and the WPF text is black, so only the "Check again" button shows. Fix on PR #6 branch: set the palette's background/foreground explicitly from the AutoCAD colour theme (COLORTHEME 0 = dark → light text, 1 = light → dark text), so the text is readable in both themes. Also set the Core assembly version (shows 1.0.0). Owner re-checks the palette, then merges #6. | todo |

## MB — Design Brief & semi-parametric palette (D14, concept v1)
| # | Item | Status |
|---|---|---|
| B.1 | Design Brief schema v1 in Core: background refs, zones + character sliders (global defaults, per-zone override per D15), tagged curves (walkingNetwork/contour/flowBasin/border), tagged points (entrance/interestPoint/elevation/tree), style (images + prompt); JSON read/write, validator, sample, tests | review — PR (Core-only, D13) |
| B.2 | Metrics on a Design Intent (Core): hard/soft share, canopy cover, curved/straight share, accessible share, steepest slopes; compare to slider targets with tolerance → "target vs achieved" | todo |
| B.3 | Design Intent v1: zone refs, trees (new + kept), drainage (swale, basin, low points), site elements, interest-point places | todo |
| B.4 | Style profile: reference images + prompt → material palette, planting character, form language (Claude vision), cached | todo |
| B.5 | Plan stage: brief + style profile + site → Design Intent spaces/paths; repair loop on metric misses and rule failures (extends 1.2); 3 layout variants per run, only the chosen one is detailed (D18) | todo |
| B.6 | Grading solver v0 (Core): levels from entrances/FFL, elevation points, contours, survey; contours are soft unless given a level (D17); ground steeper than max grade becomes a retaining wall/terrace (D16); cut/fill target; max slope caps non-accessible paths (D19); reasons sent back to Plan | todo |
| B.7 | Civil 3D palette, five panels per sketch: pick & tag objects (tags stored on the objects), tag colours, sliders, style image slots, background assignment → Design Brief | todo — after 0.3 |
| B.8 | Generate → variants → target-vs-achieved card + rule report → Apply in one undo (replaces 1.4) | todo — after B.5, B.7 |

## M0 — Foundations
| # | Item | Status |
|---|---|---|
| 0.1 | Solution skeleton: `Core` (no Autodesk refs), `Civil3DPlugin`, `Tests`; Core builds and tests on Linux | done |
| 0.2 | GitHub Actions: build + test on every PR | done |
| 0.3 | Plugin load (bundle/PackageContents.xml), ribbon tab, empty palette, `AIHELLO` command (owner tests), Civil 3D 2027 | test — PR #6, owner tested 2026-10-26: loads, `AIHELLO` OK, Civil 3D ready, tab survives workspace switch; **palette text invisible (bug 0.3a)** |
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
| 2.1a | Existing-tree table import (CSV, D4b c): separator/quote handling, column mapping proposal (English + Hebrew headings), units read from headings or asked, coordinate system asked, per-row problems, unit sanity check | done |
| 2.1b | Import survey from DWG: points, breaklines, existing surface, lot borders, buildings/FFLs, trees as attributed blocks (field mapping shared with 2.1a); read drawing units/coordinate system or ask (D4b) | todo |
| 2.2a | Rule engine, first slice: rule-set file format + loader, default starter set (D3), checks for path slope/cross fall, plaza/lawn min fall + max fall/slope, bed max slope; pass/fail/not-checked report with zoom points | done |
| 2.2b | Rule engine, second slice: ramp/step geometry (landings, riser/tread, handrail zones), tree protection zones, setbacks (needs trees + setbacks in the intent or from 2.1) | todo |
| 2.1c | Tree table: read .xlsx directly, same field mapping as CSV; trunk = diameter (D12) | done |
| 2.2c | Rule set: mark D10 values verified; Design Intent path flag "not accessible", skipped by accessible-route rules (D11) | done |
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
