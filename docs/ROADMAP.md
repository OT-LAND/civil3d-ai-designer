# Roadmap

Status keys: `todo` · `doing` · `review` (PR open) · `test` (needs owner's Civil 3D test) · `done` · `blocked:D#`

## M0 — Foundations
| # | Item | Status |
|---|---|---|
| 0.1 | Solution skeleton: `Core` (no Autodesk refs), `Civil3DPlugin`, `Tests`; Core builds and tests on Linux | todo |
| 0.2 | GitHub Actions: build + test on every PR | todo |
| 0.3 | Plugin load (bundle/PackageContents.xml), ribbon tab, empty palette, `AIHELLO` command (owner tests) | blocked:D1 |
| 0.4 | Claude API client in `Core` (key from settings, retries, image input, structured JSON output) | blocked:D2 |

## M1 — Design Intent pipeline (text → simple site)
| # | Item | Status |
|---|---|---|
| 1.1 | Design Intent JSON schema v0: site, surfaces, parcels, road centrelines, pads, design rules | todo |
| 1.2 | Prompt + validator: free text → Design Intent | blocked:D2 |
| 1.3 | Builders: existing surface from points/breaklines; parcels from lot lines | blocked:D1 |
| 1.4 | Preview-and-confirm UI before writing to the drawing | todo |

## M2 — Background data & constraints
| # | Item | Status |
|---|---|---|
| 2.1 | Import survey and lot borders (formats per D4) | blocked:D4 |
| 2.2 | Constraint engine: setbacks, easements, max slopes, cover depths, flood/levels | blocked:D5 |
| 2.3 | Constraint report palette (pass/fail per rule, zoom to violation) | todo |

## M3 — Visual inputs
| # | Item | Status |
|---|---|---|
| 3.1 | Sketch/plan image → layout intent (roads, pads, lots) with georeference/scale calibration | todo |
| 3.2 | Diagram/section sketch → typical cross-section → assembly | todo |
| 3.3 | Multi-input fusion: text + sketches + background into one Design Intent | todo |

## M4 — Detailed design
| # | Item | Status |
|---|---|---|
| 4.1 | Alignments + profiles from intent, checked against design rules | todo |
| 4.2 | Assemblies + corridors, corridor surface, tie-in to existing ground | todo |
| 4.3 | Grading: feature lines, pads, grading groups; cut/fill volumes | todo |
| 4.4 | Gravity pipe network (storm/sewer) and pressure network (water) | blocked:D6 |
| 4.5 | Styles, label sets and parts lists from template | blocked:D3 |

## M5 — Designer UX
| # | Item | Status |
|---|---|---|
| 5.1 | Template manager: .dwt, styles, label sets, assemblies, design-rule sets | blocked:D3 |
| 5.2 | Post-generation editing by instruction ("widen road A to 7 m") | todo |
| 5.3 | Generation history: compare versions, roll back | todo |
