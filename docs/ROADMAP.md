# Roadmap

Status keys: `todo` · `doing` · `review` (PR open) · `test` (needs owner's Civil 3D test) · `done` · `blocked:D#`

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
| 1.4 | Preview-and-confirm UI before writing to the drawing | todo |

## M2 — Background data & constraints
| # | Item | Status |
|---|---|---|
| 2.1 | Import survey, existing trees, lot borders, buildings/FFLs (DWG survey first; trees as DWG blocks or separate table with field mapping; read units/coordinate system or ask, per D4b) | todo |
| 2.2a | Rule engine, first slice: rule-set file format + loader, default starter set (D3), checks for path slope/cross fall, plaza/lawn min fall + max fall/slope, bed max slope; pass/fail/not-checked report with zoom points | review |
| 2.2b | Rule engine, second slice: ramp/step geometry (landings, riser/tread, handrail zones), tree protection zones, setbacks (needs trees + setbacks in the intent or from 2.1) | todo |
| 2.3 | Constraint report palette (pass/fail per rule, zoom to problem) | todo |

## M3 — Visual inputs
| # | Item | Status |
|---|---|---|
| 3.1 | Hand sketch over site plan → spaces and edges, with scale/georeference calibration | todo |
| 3.2 | Concept diagram → zoning, circulation and level intent | todo |
| 3.3 | Render/reference image → material palette and planting character | todo |
| 3.4 | Multi-input fusion: text + sketches + images + background into one Design Intent | todo |

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
