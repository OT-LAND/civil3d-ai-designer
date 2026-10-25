# Concept v1: Semi-parametric generative designer

Status: agreed 2026-10-25 from the owner's UI sketch (D14–D18, D13). Max slope: D19. This file is the product concept; `ARCHITECTURE.md` is how it is built.

## 1. The idea in one paragraph
The designer does not draw the design and does not write a long prompt. They **pick loose
geometry** already in the drawing (polygons, curves, points), **tag** each piece with a general
intent ("walking network", "entrance", "flow basin"…), set a few **character sliders** for the
polygons (hard ↔ soft, cut ↔ fill, curved ↔ straight…), point at the **background** (survey,
structures, roads, orthophoto) and give a **visual style** (reference images + a text prompt).
The AI turns that brief into a detailed, rule-checked landscape model. The same brief can drive
Civil 3D, Revit or another host: only the capture and build adapters change.

## 2. The UI (from the sketch)
One palette, five panels, top to bottom:

| Panel | What the designer does | Becomes in the brief |
|---|---|---|
| **Style** | Drops up to 6 reference images (mood, materials, planting) and/or writes a text prompt | `style.images[]`, `style.prompt` |
| **Polygon** | Picks closed polylines/regions = design zones. Sets character sliders (global defaults, any zone can override single sliders, D15) | `zones[]` + `character` |
| **Curves** | Picks open polylines/arcs/splines and tags each: walking network · contour · flow basin · border | `curves[]` with `tag` |
| **Points** | Picks points/blocks and tags each: entrance · interest point · elevation point · tree | `points[]` with `tag` |
| **Background** | Assigns layers/objects/xrefs/images as: survey · structures · roads · orthophoto | `background` (read-only context) |

Bottom of the palette: **Generate** → 2–3 variants → per variant a *target vs achieved* card
(e.g. hard/soft 40/60 target → 43/57 achieved) and the rule report → **Apply** (one undo).

### Polygon character sliders (0–100, each with two ends)
| Slider | Left end (0) | Right end (100) | How the generator uses it |
|---|---|---|---|
| Accessibility | legal minimum only | everything step-free | share of the zone's spaces reached by accessible routes; legal minimum (D10/D11) is always enforced |
| Hardscape ↔ Softscape | all paved | all planted | target share of zone area paved vs planted (lawn + beds), ±5 % |
| Max grade | gentle | rule-set maximum | steepest graded ground before a retaining wall is used; steeper level changes become walls/terraces (D16) |
| Cut ↔ Fill | export soil | import soil | earthwork balance target; 50 = balanced on site |
| Max slope | gentle | rule-set maximum | steepest walking surface on paths marked not accessible (D19); accessible routes follow the rule set |
| Shading | open / sunny | full canopy | target canopy cover at maturity (trees + shade structures) |
| Trees ↔ Elements | trees only | built elements only | how vertical elements and shade are made: trees vs pergolas, shade sails, furniture, play |
| Curved ↔ Straight | organic / curvilinear | orthogonal / rectilinear | geometric language of edges, paths and beds |

Sliders are **targets** the generator must meet and the checker measures, not prompt adjectives.
That is what makes the tool *semi-parametric*: the AI is free in layout, form and species, but
every slider is a number with a tolerance, and the result reports how close it got.

### Curve tags
- **Walking network**: the circulation spine. Becomes paths (centerline + width from the
  rule set and style), connected to entrances and interest points. Accessible unless the
  designer marks it otherwise (D11).
- **Contour**: a landform gesture: "the ground should follow this line". The designer can
  give it a level (then it is hard); otherwise it is soft (D17).
- **Flow basin**: where water should go: a swale or flow line, ending in a retention or
  infiltration area at its low end (D17).
- **Border**: an edge the design must respect or build: lot border, wall, kerb, hedge line.

### Point tags
- **Entrance**: fixed level tie-in (threshold/FFL) and a node the walking network must reach.
- **Interest point**: a destination or focal point: the network leads there, and a special
  place (seat, tree, water, art) is designed around it.
- **Elevation point**: a hard spot level.
- **Tree**: "a tree here". The AI chooses the species from the plant list (D5) unless the
  designer gives one. Existing trees come from Background, with protection zones.

## 3. The AI strategy behind it (host-agnostic)

### 3.1 Three documents
```
 Capture (host)          Generate (Core + Claude)                 Build (host)
 selection + tags  ->  DESIGN BRIEF  ->  staged AI + solvers  ->  DESIGN INTENT  ->  Civil 3D / Revit
                       (what the            ^          |           (what to build,     builders
                        designer wants)     |  repair  v            in spaces/levels)
                                          METRICS + RULE REPORT
```
- **Design Brief** (new, input): tagged geometry, sliders, style, background references.
  Host-neutral: metres, 2D/3D points, ids that point back to host objects.
- **Design Intent** (existing v0, extended to v1): spaces, levels, edges, materials, plus
  planting, drainage and elements. Still the only thing builders consume.
- **Metrics + rule report** (new): numbers measured on a Design Intent: hard/soft share,
  canopy cover, cut/fill, curvature share, accessible share, steepest slopes. Each is
  compared to its slider target and to the rule set.

### 3.2 Who does what
| Job | Done by | Why |
|---|---|---|
| Reading style images → materials, planting character, form language | Claude (vision) | interpretation |
| Zoning each polygon into spaces that hit the hard/soft and shading targets | Claude, checked by metrics | creative layout |
| Path layout along the walking network, links to entrances and interest points | Claude proposes, Core snaps and widens | topology is creative, widths are rules |
| Levels: spot levels, falls, tie-ins, landform | **Core solver** from constraints (elevation points, entrances/FFL, contours, survey); Claude only chooses the strategy (terraces vs continuous slope, where the low point is) | grading must be exact and repeatable |
| Cut/fill, slopes, accessibility, protection zones | **Core** (rule engine, metrics) | deterministic, testable |
| Species and tree positions | Claude from the user's plant list; Core checks spacing and protection zones | D5 |
| Civil 3D objects | **Builders** (feature lines, grading, corridors, pipe networks) | AI never edits the drawing |

### 3.3 Staged generation (each stage is small, validated, and re-runnable)
1. **Read**: the brief + background → a site summary (levels, buildings, trees, constraints).
2. **Style**: images + prompt → a *style profile*: material palette, planting character,
   form language. Cached; reused across runs.
3. **Plan**: per zone, spaces and paths (the zoning) → measured against the hard/soft,
   shading, curved/straight and accessibility targets. Misses go back as a repair prompt
   (as in 1.2). Produces **3 layout variants** at this stage (D18).
4. **Level**: Core grading solver sets levels from hard constraints, then within the
   cut/fill target; ground steeper than max grade becomes a retaining wall or terrace. Failures go back to Plan with the reason
   ("plaza can't reach entrance E1 within 5 %, needs a ramp").
5. **Detail**: steps, ramps, walls, edges, drainage along flow basins, planting, elements.
6. **Check**: rule report + target vs achieved. Shown to the designer before Apply.

Only stages 2, 3 and 5 call Claude; 1, 4 and 6 are pure Core. Variants stop at stage 3
until the designer picks one, which keeps cost down on the designer's own key (D2).

### 3.4 Editing and re-running
- Tags live on the drawing objects (Civil 3D: extension dictionary/XData), so the brief is
  re-read from the drawing at any time. Moving a tagged curve and pressing Generate again
  re-runs from the first stage it affects.
- Locking: a generated space can be locked; the next run keeps it as a constraint.
- Edit-by-instruction (5.2) changes the brief or a locked space, not raw geometry.

### 3.5 Other hosts
`Capture` and `Build` are the only host-specific parts. Revit later: tagged model lines,
floors/regions and points → same brief; the same Design Intent → toposolid, floors, planting
families. The MCP connector (D9, 5.4) exposes the same brief → intent tools.

## 4. What changes in the existing work
Kept as is: Claude client (0.4), Design Intent v0 (1.1, extended), text → intent repair
loop (1.2, becomes the Plan stage), rule engine (2.2), tree table import (2.1), plugin
load (0.3). Redirected: sketch recognition (3.1) and concept diagrams (3.2) are replaced
by tagged geometry; images (3.3) become the Style panel. New: Design Brief, metrics,
grading solver, the five-panel palette.

## 5. Example brief (shape, not final)
```json
{
  "schemaVersion": "1",
  "units": "m",
  "background": { "survey": ["layer:V-TOPO-*"], "structures": ["layer:A-BLDG"],
                  "roads": ["layer:C-ROAD"], "orthophoto": ["image:ortho_2025.tif"] },
  "style": { "prompt": "shaded Mediterranean courtyard, local stone, dry planting",
             "images": ["ref1.jpg", "ref2.jpg"] },
  "character": { "accessibility": 70, "hardSoft": 55, "maxGrade": 30, "cutFill": 50,
                 "maxSlope": 40, "shading": 65, "treesElements": 30, "curvedStraight": 35 },
  "zones": [ { "id": "Z1", "outline": [[0,0],[40,0],[40,25],[0,25]],
               "character": { "hardSoft": 30 } } ],
  "curves": [ { "id": "C1", "tag": "walkingNetwork", "points": [[2,12],[20,14],[38,10]] },
              { "id": "C2", "tag": "flowBasin", "points": [[30,2],[36,6]] } ],
  "points": [ { "id": "P1", "tag": "entrance", "at": [2,12], "level": 101.20 },
              { "id": "P2", "tag": "interestPoint", "at": [24,18] },
              { "id": "P3", "tag": "elevation", "at": [38,10,100.40] },
              { "id": "P4", "tag": "tree", "at": [12,20] } ]
}
```
