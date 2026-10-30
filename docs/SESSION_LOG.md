# Session log (newest first)

## 2026-10-30 — Work: B.6c Grading solver, cut/fill
- Done: `Core/Grading/Earthworks`. Cut and fill of the solved levels against existing ground: plazas, lawns and beds
  sampled on a 0.5 m grid inside the outline, paths along the centerline × width; ground without data counted as not
  measured. `Balance` moves the spaces nothing fixes (no hard level, no nominal level) up or down together, as little as
  needed (≤ 0.30 m), toward the cut↔fill slider read as fill share (0 export, 50 balanced, 100 import); falls stay, so
  B.6b checks still hold; out of reach ⇒ a plain-words reason with the volumes. `GradingMetrics` + target card: cutFill
  (±10) and maxSlope (at most, +5; steepest not-accessible path in slider units, D19/D22) rows now measured.
  16 new tests; `dotnet build` (Release, 0 warnings) + `dotnet test` 258/258 pass.
- Also: ROADMAP B.6b marked done (merged #21).
- Split: maxGrade row moved to B.6e (needs D23); plan review wiring stays in B.6d.
- Blocked: B.6e still waits for D23.
- PR: Core-only, merged by the agent once CI is green (D13).
- Next: B.6d (wire solvers, balance and grading reasons into the plan review), then B.6e after D23.
- Questions: none new. (Choices made, easy to change: slider = fill share of moved soil; free spaces all move by the
  same amount, max 0.30 m; paths never move; where a path crosses an area both are counted; tolerance ±10.)

## 2026-10-30 — Work: B.6b Grading solver, area space levels
- Done: `Core/Grading/AreaLevelSolver`. Every plaza, lawn and bed gets one even surface (level at its centre, fall,
  fall direction, corner levels) through its hard levels: own spot levels, entrances on its edge (threshold/FFL), brief
  entrance and elevation points, contours with a level. Where they leave the fall free: the designer's fall, else plaza
  1.5 % (middle of 1–2 %), lawn and bed follow existing ground within their limits; direction downhill, else away from
  the nearest building. Checked against plaza min/max fall, lawn min fall and 1:4 mowing max, bed 1:3; reasons in plain
  words (too flat to drain, needs steps/terraces, needs a wall or terraces, levels not on one surface, no level).
  15 new tests; `dotnet build` (Release, 0 warnings) + `dotnet test` 242/242 pass.
- Also: D22 rule `path.notAccessibleMaxSlopePercent` marked verified; ROADMAP B.6a marked done (merged).
- Split: soft contours, walls from max grade and path–plaza level hand-over moved to new item B.6e.
- Blocked: B.6e walls need D23 (what max grade slider 0 means).
- PR: Core-only, merged by the agent once CI is green (D13).
- Next: B.6c cut/fill (not blocked), then B.6e after D23.
- Questions: D23. (Choices made, easy to change: plaza design fall = middle of the rule range; one plane per space,
  terraces come with B.6e; points count for a space within 1 m of its outline; paths and areas solved separately.)

## 2026-10-30 — Meeting 13
- Reported: B.1b cost level (#19) and B.6a path levels (#20), both Core-only and merged (D13); next B.6b. PR #6 re-tested OK, waiting for the owner to merge.
- Asked: D22 (steepest not-accessible path; recommended 1:8 = 12.5 %).
- Owner: "Merge PR #6". Main merged into `agent/plugin-load` (docs conflicts only), `dotnet build` (0 warnings) + `dotnet test` 227/227 pass, PR #6 squash-merged. ROADMAP 0.3 done.
- Answers: D22 (a) 1:8 = 12.5 % → Decided; rule to be marked verified next work session.

## 2026-10-29 — Work: B.6a Grading solver v0, path levels
- Done: `Core/Grading/PathLevelSolver` + `SpotGround`/`IGroundModel`. Levels along every path from the hard
  constraints on it (spot levels, entrances threshold/FFL, brief entrance/elevation points, contours with a level),
  even grade between them, ends tie into existing ground or hold. Each stretch checked: accessible limit 5 % (D10/D11),
  not-accessible paths by the max slope slider (D19) from 5 % to the new rule `path.notAccessibleMaxSlopePercent`.
  Issues in plain words: needs a ramp / steps with the route length that works, conflicting levels, no level.
  15 new tests; `dotnet build` (Release, 0 warnings) + `dotnet test` 207/207 pass.
- Split B.6 into B.6a (this), B.6b area spaces + walls from max grade + soft contours, B.6c cut/fill + target rows,
  B.6d reasons into the plan repair loop.
- Also: ROADMAP B.1b marked done (merged).
- Blocked: nothing.
- PR: Core-only, merged by the agent once CI is green (D13).
- Next: B.6b.
- Questions: D22 (steepest not-accessible path; provisional 12.5 % = 1:8, rule marked not verified).
  (Choices made, easy to change: snap = half path width + 1 m; path ends with no fixed level meet existing ground,
  since area-space levels come in B.6b; the path's nominal elevation is not used yet.)

## 2026-10-28 — Work: B.1b Cost level
- Done: Design Intent `costClass` (low · mid · high, optional) on materials, new trees (size at planting) and
  site elements; schema + sample (courtyard v1). `IntentMetrics.CostLevelPercent` per zone: classes scored
  0/50/100, surfaces by area through the material, new trees and elements by count, the parts present averaged.
  The budget row on target-vs-achieved is now measured (±15) and slider misses go back to the plan as repair.
  Plan prompt `plan.v4`: give every material, new tree and element a cost class. 11 new tests; `dotnet build`
  (Release, 0 warnings) + `dotnet test` 192/192 pass.
- Also: ROADMAP B.5b marked done (merged).
- Blocked: nothing.
- PR #19: Core-only, merged by the agent once CI is green (D13).
- Not verified: whether Claude picks sensible cost classes (needs the owner's key); first check when B.8 wires Generate.
- Next: B.6 grading solver v0.
- Questions: none new. (Choices made, easy to change: no material/plant library exists yet, so the cost class
  sits on the Design Intent items and Claude sets it; when office libraries come (5.1) the class moves there.
  Equal weight for surfaces, trees and elements; walls and steps not counted yet; tolerance ±15 so an all-mid
  design meets budgets 35–65; kept trees and unclassed items are left out.)

## 2026-10-28 — Work: B.5b Plan stage: three layout options
- Done: `PlanStage.GenerateOptionsAsync(request, count = 3)` → `PlanOptions` (D18). Options are planned one after
  another; each prompt lists the concepts of the usable options so far and asks for a clearly different layout
  (main open space, path routes, grouping/shape). Each option is reviewed and repaired as in B.5a. Every plan
  now carries a one-line `concept` (required in the tool schema, prompt `plan.v3`); without one it is named
  "Option n". A failed option is listed in `Failed` and the others are returned; none usable throws.
  6 new tests; `dotnet build` (Release, 0 warnings) + `dotnet test` 181/181 pass.
- Also: ROADMAP B.1a marked done (merged).
- Blocked: nothing.
- PR #18: Core-only, merged by the agent once CI is green (D13).
- Not verified: real Claude options on the sample brief (needs the owner's key); first check when B.8 wires Generate.
- Next: B.1b cost level, or B.6 grading solver v0.
- Questions: none new. (Choices made, easy to change: options run one after another so each sees the earlier
  concepts (slower than parallel, but more distinct); up to 3 calls per option, 9 per Generate at worst;
  a missing concept is never repaired.)

## 2026-10-28 — Work: B.1a Brief: budget slider, parcel + utilities
- Done: `Character.Budget` (9th slider, 0 economical – 100 premium, global required + zone override, D15/D20);
  `BackgroundRefs.Parcel` and `.Utilities` (D21, palette order survey · parcel · structures · roads · utilities ·
  orthophoto); schema, validator, sample (global budget 40, entry court 70, parcel + utilities layers).
  Plan prompt `plan.v2`: budget per zone in words (`PlanStage.BudgetWords`, 5 bands), utilities/easements
  keep-clear. Target-vs-achieved gets a `budget` row, "not measured" until B.1b. 9 new tests; `dotnet build`
  (Release, 0 warnings) + `dotnet test` 175/175 pass.
- Split B.1a: cost classes on materials/plants and the cost-level metric moved to new item B.1b.
- Also: ROADMAP B.5a marked done (merged).
- Blocked: nothing.
- PR #17: Core-only, merged by the agent once CI is green (D13).
- Next: B.1b cost level, or B.5b plan variants.
- Questions: none new. (Choices made, easy to change: budget is required in the global set like the other
  sliders, so older briefs without it are reported; budget words in 5 bands (≤20, ≤40, <60, <80, ≥80);
  over utilities the plan avoids trees, walls and deep beds but allows paving and lawn.)

## 2026-10-28 — Meeting 12
- Reported: B.4 style profile (PR #15) and B.5a plan stage (PR #16), both Core-only and merged (D13); next B.5b. PR #6 still waits on bug 0.3a fix, then the owner re-checks.
- Asked: nothing (no open questions).
- Owner asked for a budget slider and parcel/utilities in Background → decided D20, D21; new item B.1a.
- Correction: 0.3a was already fixed on PR #6 (2026-10-26); ROADMAP said todo. Now `test`; owner given the re-test steps.
- Owner re-tested 0.3a in Civil 3D: palette text readable in dark and light theme, versions 0.3.1. Recorded on PR #6; 0.3a done.

## 2026-10-27 — Work: B.5a Plan stage (one plan)
- Done: `Core/Plan/PlanStage`: brief + style profile + site → one Design Intent (system prompt `plan.v1`, sliders
  per zone written out in plain words, forced JSON as 1.2). `PlanStage.Review` checks each answer: brief refs
  (zones, interest points each get exactly one place) are blocking problems; rule failures and slider misses
  ("zone Z2 (Garden): hardSoft target 62, achieved 0 … raise it") are open issues sent back as repair. After the
  last call the best usable plan is returned with its open issues. 11 new tests; `dotnet build` (Release,
  0 warnings) + `dotnet test` 166/166 pass.
- Split B.5 into B.5a (this) and B.5b (3 variants, D18).
- Also: ROADMAP B.4 marked done (merged).
- Blocked: nothing.
- PR #16: Core-only, merged by the agent once CI is green (D13).
- Not verified: a real Claude plan on the sample brief (needs the owner's key); first check when B.8 wires Generate.
- Next: B.5b plan variants.
- Questions: none new. (Choices made, easy to change: slider misses never block, the designer sees them on the
  card; 3 calls max; entrance reach and walking-network following are not checked yet (B.6 / later); existing
  trees are not passed to the plan yet.)

## 2026-10-27 — Work: B.4 Style profile
- Done: `Core/Style`: `StyleProfile` (summary, material palette ≤12 → Design Intent materials, planting character
  (water use, density, layers, palette, example genera), form language (geometry, curved share 0–100, edges,
  level changes), one note per image), JSON + embedded schema `style-profile.v1.schema.json`, validator;
  `StyleReader` (Claude vision: labelled images + prompt, system prompt `style-reader.v1`, forced JSON, repair
  loop) and `StyleProfileCache` (file per SHA-256 of prompt version + prompt + image bytes). Sample
  `samples/style/courtyard.style.v1.json`. 11 new tests; `dotnet build` (Release, 0 warnings) + `dotnet test` 155/155 pass.
- Also: ROADMAP B.3a marked done (merged).
- Blocked: nothing.
- PR #15: Core-only, merged by the agent once CI is green (D13).
- Not verified: a real Claude call on real reference images (needs the owner's key); first check when B.7/B.8 wire the Style panel.
- Next: B.5 plan stage.
- Questions: none new. (Choices made, easy to change: max 2 Claude calls for style; cache ignores file names
  and model; the style's curved share is only a lean, the brief's curved↔straight slider stays the target;
  example plants are suggestions, the plant list decides (D5).)

## 2026-10-27 — Meeting 11
- Reported: B.2 metrics (PR #12), B.3 Design Intent v1 (PR #13), B.3a canopy/trees-vs-elements metrics (PR #14), all Core-only and merged (D13); PR #6 palette fix 0.3a still awaits the owner's re-check in Civil 3D. Next: B.4.
- Asked: nothing (no open questions).

## 2026-10-26 — Work: B.3a Canopy cover + trees vs elements metrics
- Done: `IntentMetrics` now measures canopy cover (tree canopies at maturity / as surveyed + shade and pergola
  footprints, overlaps counted once, clipped to the zone outline or else the lot boundary, building footprints
  left out; new `Core/Metrics/CoverArea` grid sampler) and the elements share of new trees + site elements
  (by count, location in the zone). `TargetComparison` compares them to the shading and trees↔elements sliders
  (±10). 7 new tests; `dotnet build` (Release, 0 warnings) + `dotnet test` 144/144 pass.
- Blocked: nothing.
- PR #14: Core-only, merged by the agent once CI is green (D13).
- Next: B.4 style profile.
- Questions: none new. (Choices made, easy to change: trees vs elements counts items, not area; kept trees are
  not counted there but do count for canopy; lighting counts as an element; tolerance ±10 for both sliders;
  no lot and no zone → canopy not measured.)

## 2026-10-26 — Work: B.3 Design Intent v1
- Done: `Core/Intent` v1 (schema `design-intent.v1.schema.json`, new intents default to "1", "0" still read):
  spaces get `zoneId` / `interestPointId`; `trees` (new/existing, canopy, trunk, protection radius, `sourceId`);
  `drainage` (swales with outlet → basin/low point, retention/infiltration basins, low points with inlet flag);
  `elements` (8 kinds, shade/pergola need a footprint). Validator checks the new parts and that "0" documents
  don't use them; TextToIntent keeps the new parts when it swaps in the surveyed site. Sample
  `samples/intent/courtyard.v1.json` (matches the B.1 brief). 14 new tests; `dotnet build` (Release, 0 warnings)
  + `dotnet test` 137/137 pass.
- Blocked: nothing.
- PR #13: Core-only, merged by the agent once CI is green (D13).
- Next: B.3a canopy cover + trees vs elements metrics (small), then B.4 style profile.
- Questions: none new. (Choices made, easy to change: existing trees must carry their survey id; brief refs
  are not checked inside the intent (the brief is a separate file); the text-to-intent prompt is still v0 and
  will be rewritten in B.5.)

## 2026-10-26 — Work: B.2 Metrics on a Design Intent
- Done: `Core/Metrics`: `IntentMetricsCalculator.Measure(intent, zone?)` (hard/soft share, curved/straight share,
  accessible share of path length, steepest slope on accessible paths / other paths / plazas / lawns+beds, optional
  zone filter) and `TargetComparison.Compare(metrics, CharacterFor(zone))` → target vs achieved per slider
  (Met/Missed/NotMeasured). `Core/Geometry/PolygonGeometry` helpers. 11 new tests; `dotnet build` (Release,
  0 warnings) + `dotnet test` 123/123 pass.
- Not measured yet (reported as such): canopy cover, trees vs elements (need B.3); max grade, cut/fill, max slope
  targets (need B.6 slider → slope mapping).
- Blocked: nothing.
- PR #12: merged by the agent once CI is green (D13).
- Next: B.3 Design Intent v1.
- Questions: none new. (Choices made, easy to change: curved/straight tolerance ±10, accessibility is a minimum
  with −5; accessibility measured as accessible share of path length until B.3/B.5 give route connectivity.)

## 2026-10-26 — Meeting 10
- Reported: B.1 Design Brief schema (PR #11, Core-only, agent merges per D13); PR #6 palette fix 0.3a awaits the owner's re-check in Civil 3D. Next: B.2.
- Asked: nothing (no open questions).

## 2026-10-26 — Work: B.1 Design Brief schema v1
- Done: `Core/Brief`: `DesignBrief` model (background refs, style prompt + ≤6 images, 8 global sliders, zones with
  per-zone overrides (D15), tagged curves and points, `hostId` back to the drawing object), `CharacterFor(zone)`,
  `DesignBriefJson` read/write, embedded schema `design-brief.v1.schema.json`, `DesignBriefValidator`
  (ids, geometry, slider 0–100, complete global set, level only on contour/entrance/elevation, elevation needs a
  level, accessible only on walking network, species only on trees, metres only). Sample
  `samples/brief/courtyard.brief.v1.json`. 11 new tests; `dotnet build` (Release, 0 warnings) + `dotnet test` 112/112 pass.
- Note: bug 0.3a was already fixed on PR #6 earlier today; #6 waits for the owner's palette re-check.
- Blocked: nothing.
- PR #11: CI green, merged by the agent (D13).
- Next: B.2 metrics on a Design Intent (Core).
- Questions: none new. (Choices made, easy to change: brief points use `{x, y}` like the intent, not `[x, y]`
  as in the CONCEPT example; levels are a separate `level` field.)

## 2026-10-26 — Work: bug 0.3a palette text invisible (on PR #6)
- Done: main merged into `agent/plugin-load` (merge commit; docs conflicts resolved, main's ROADMAP/LOG kept, both ARCHITECTURE sections kept).
  Core `Ui/PaletteTheme`: COLORTHEME 0/missing → dark (#3B4453 bg, light text), 1 → light (dark text); contrast checked in tests
  (main text ≥ 7:1, hint ≥ 4.5:1). Palette sets Background/Foreground explicitly and re-applies on a COLORTHEME change.
  Core assembly version set; Core + plugin + bundle now 0.3.1. 12 new tests; `dotnet build` (Release, 0 warnings, plugin
  compiled against AutoCAD.NET 26.0.0) + `dotnet test` 121/121 pass.
- Blocked: nothing. (.NET 8 + 10 SDKs installed with dotnet-install.sh.)
- Next: owner re-checks the palette in both themes and merges #6; then B.1 Design Brief schema v1.
- Questions: none new.

## 2026-10-26 — Meeting 9: PR #6 Civil 3D test
- Owner built PR #6 (after installing the .NET 10 SDK; the zip SDK was shadowed on Path, the installer fixed it) and loaded it in Civil 3D 2027.
- Result: plugin loads, `AIHELLO` prints all 4 lines: host 26.0.0.0, "Civil 3D: ready.", Claude "no API key yet" (expected; no key set). Palette opens but shows only the "Check again" button on a black background: text is black on black (bug 0.3a, top of backlog). AI Landscape tab stays after a workspace switch.
- Note: Core shows version 1.0.0 (Core assembly has no version set); cosmetic, fix with the next plugin PR.
- Build note for the PR/README: needs the .NET 10 **SDK installer** (`dotnet --list-sdks` must show 10.x).

## 2026-10-26 — Meeting 9
- Reported: #7→#9 and #8→#10 merged (D13); concept v1 + D15–D19 recorded; next B.1 Design Brief schema. PR #6 (0.3 plugin load) still awaits the owner's Civil 3D test.
- Asked: nothing (no open questions).

## 2026-10-25 — Work: merge Core-only stacks #7→#9 and #8→#10 (D13)
- Done: main merged into `agent/rule-set-d10-d11` (PR #9) and then into `agent/tree-table-xlsx` (PR #10);
  docs conflicts resolved (both ARCHITECTURE sections kept, D12 dropped from Open, M2 rows combined, log by date).
  PRs retargeted to main and merged once CI is green; #7 and #8 come in with their stacks.
  `dotnet build` (Release, 0 warnings) + `dotnet test` 101/101 pass. 2.1a, 2.1c, 2.2a, 2.2c → done.
- Blocked: nothing. (.NET 8 SDK installed with dotnet-install.sh; apt not needed.)
- Next: B.1 Design Brief schema v1 (Core).
- Questions: none new.

## 2026-10-25 — Owner input: UI sketch → concept v1
- Owner sent a hand-drawn palette sketch (Style / Polygon sliders / Curves / Points / Background tags) and asked to
  re-aim the product as a semi-parametric, host-agnostic generative designer, concept first.
- Done (docs only): `docs/CONCEPT.md`, D14 decided, new milestone MB (B.1–B.8) on top of the roadmap,
  3.1/3.2/3.4/1.4 superseded, palette mockup `docs/ui/palette-mockup.html`.
- Asked: D15 (sliders global/per zone), D16 (max grade meaning), D17 (contour, flow basin), D18 (options per run); D13 still open.
- Next work item: B.1 Design Brief schema (not blocked).
- Answers (same day): D15 c, D16 max grade = steepest ground before a retaining wall, D17 contour soft / flow basin to basin,
  D18 three layout options, D13 b (agent merges Core-only PRs). New open: D19 (what max slope means). B.6 unblocked.
- D19 answered: (b) max slope = steepest walking surface on non-accessible paths. No open questions.

## 2026-10-25 — Meeting 8 (evening)
- Nothing new since Meeting 7. Re-sent status (#7→#9, #8→#10 review; #6 Civil 3D test) and D13 (recommended b). No answer yet.

## 2026-10-25 — Meeting 7
- Reported: PR #9 (2.2c) and PR #10 (2.1c) built; review stacks #7→#9 and #8→#10; PR #6 (0.3) still awaits the owner's Civil 3D test.
- Asked: D13 (who approves PRs; recommended b, agent merges Core-only PRs when tests pass).

## 2026-10-24 — Work: 2.1c Tree tables from Excel (.xlsx)
- Done: `Core/Survey/XlsxTable.Read(stream, sheet?)` reads an .xlsx sheet into the same `CsvTable`, so mapping,
  unit questions and import are unchanged. One package added (ExcelDataReader 3.7.0, MIT). Several sheets and
  none chosen → "which sheet holds the trees?" (never guessed); hidden sheets ignored; Excel row numbers kept;
  numbers read from cell values (text numbers still accepted); non-.xlsx file → clear error. Samples
  `trees-courtyard.xlsx` (same trees as the CSV + a Notes sheet) and `trees-hebrew.xlsx`. 6 new tests;
  `dotnet build` (Release, 0 warnings) + `dotnet test` 88/88 pass.
- Branch `agent/tree-table-xlsx` is stacked on PR #8 (main merged in for the Meeting 6 docs).
- Blocked: nothing. (.NET 8 SDK installed from apt again; `apt-get update` needed first.)
- Next: 2.2b (ramp/step geometry, tree protection zones, setbacks) once PR #7 merges; Civil 3D items wait on PR #6 test.
- Questions: none new. Old binary .xls is not read (save as .xlsx or CSV); say so if that is a problem.

## 2026-10-23 — Work: 2.2c Rule set verified (D10) + "not accessible" path flag (D11)
- Done: all 7 default rules marked `verified: true` with "confirmed by owner 2026-10-23 (D10)" in the source note.
  Design Intent: path-only `accessible` flag (omitted = accessible); validator rejects it on non-paths; schema and
  text-to-intent prompt updated. `RuleEngine` reports a path marked not accessible as "not checked" for the
  accessible-route rules. 2 new tests; `dotnet build` (Release, 0 warnings) + `dotnet test` 78/78 pass.
- Branch `agent/rule-set-d10-d11` is stacked on PR #7 (main merged in to pick up D10–D12 docs).
- Blocked: nothing.
- Next: 2.1c (.xlsx tree tables) on top of PR #8; 2.2b after that.
- Questions: none new.

## 2026-10-23 — Meeting 6
- Asked: D10 (default accessibility values), D11 (which paths are accessible routes), D12 (Excel tree tables; trunk diameter vs circumference). D10/D11 are open on PR #7, D12 on PR #8.
- Decided: D10 (a, values confirmed), D11 (a, all paths accessible unless marked), D12 (.xlsx read directly; trunk = diameter). New items 2.1c, 2.2c.
- Reported: PR #8 (2.1a tree-table import) and PR #7 (2.2a rule engine) await review; PR #6 (0.3 plugin load, .NET 10) still awaits the owner's Civil 3D test.

## 2026-10-22 — Work: 2.1a Existing-tree table import (CSV)
- Done: `Core/Survey`: `CsvTable` (comma/semicolon/tab detected, quotes, BOM, line numbers kept), `ExistingTree`,
  `TreeFieldMapping.Suggest` (English + Hebrew survey headings), `TreeTableUnits.Suggest` (units only from
  headings like "Trunk (cm)"), `TreeTableImport.Import`: unknown units / coordinate system / missing X-Y column
  → questions for the designer and no trees (D4b: never assume); decimal comma accepted with `;`/tab files;
  bad rows reported per line (no position → tree left out; bad size → value left empty; duplicate number and
  implausible size flagged). Sample `samples/survey/trees-courtyard.csv` (courtyard local grid).
  17 new tests; `dotnet build` (Release, 0 warnings) + `dotnet test` 82/82 pass.
- Split 2.1: DWG survey import (Civil 3D side) is 2.1b.
- Blocked: nothing. (.NET 8 SDK installed from apt again.)
- Next: 2.1b needs 0.3 (PR #6) tested; Core-only candidates: 2.2b after PR #7 merges, or D12 answer for .xlsx.
- Questions: D12 (read Excel tree tables directly, or CSV export is enough? trunk as diameter or circumference?).
  Note: this PR and PR #7 both add to DECISIONS/SESSION_LOG top; whichever merges second needs a trivial docs merge.

## 2026-10-22 — Meeting 5
- Asked: D10 (confirm default accessibility values), D11 (are all paths accessible routes?); both raised in PR #7, awaiting answers.
- Reported: PR #7 (2.2a rule engine) awaits review; PR #6 (0.3 plugin load, .NET 10) still awaits the owner's Civil 3D test.

## 2026-10-21 — Work: 2.2a Rule engine, first slice
- Done: `Core/Rules`: rule-set file format + strict loader, embedded default starter set `default-il.v0.json`
  (D3; every value has a source note, all flagged not verified), `RuleEngine` checking path slope along the
  route and cross fall, plaza min/max fall, lawn min fall / max slope, bed max slope; report is
  pass / fail / not checked per rule per space, with a zoom point and a designer-facing sentence. Courtyard
  sample passes. 11 new tests; `dotnet build` (Release, 0 warnings) + `dotnet test` 76/76 pass.
- Split 2.2: ramp/step geometry, tree protection zones and setbacks moved to new item 2.2b.
- Blocked: nothing. (.NET 8 SDK had to be installed from apt this session.)
- Next: 2.2b needs trees/setbacks in the intent (or 2.1 import); 2.3 report palette needs 0.3 (PR #6) tested first.
- Questions: D10 (confirm default accessibility values), D11 (are all paths accessible routes?).

## 2026-10-21 — Meeting 4
- Decided: none new (no open questions).
- Reported: PR #6 (0.3 plugin load, now .NET 10) waits for the owner's Civil 3D test; needs .NET 10 SDK.
- Next work item: 2.2 rule engine (Core-only) while 0.3 awaits test.

## 2026-10-19 — Meeting 3
- Decided: none new (no open questions).
- Owner approved PR #5 (1.2 free text → Design Intent); merged to main, 1.2 → done.
- Next work item: 0.3 plugin loads in Civil 3D 2027 (will carry "Please test in Civil 3D" steps).

## 2026-10-18 — Work: 1.2 Prompt + validator (free text → Design Intent)
- Done: `Core/Intent/TextToIntent` + system prompt `text-to-intent.v0.md` (embedded). Forced tool output
  `{assumptions, intent}`; shape + content validation; repair loop with the problem list (max 3 calls);
  given site always kept unchanged; assumptions returned for the designer to review. 12 new tests.
- NuGet works now (D8b): real `dotnet build` + `dotnet test` ran here: 65/65 pass, Release 0 warnings.
- Blocked: nothing.
- Next: 1.4 preview needs 0.3 (plugin load) first, so 0.3 next; or 1.3 builders (needs Civil 3D to verify).
- Questions: none new. Not yet tried against the live API (no key in the cloud); the owner can try once
  0.3/1.4 give it a button.

## 2026-10-18 — Meeting 2
- Decided: D8b (network access active), D3 (no office template; user chooses template and
  rule set, default starter set ships), D4b (trees as DWG blocks or table; units/CRS vary,
  read or ask), D5 (AI proposes species from user's plant list), D7 (test in Civil 3D only on request).
- Unblocked: 2.2, 4.3, 5.1. Updated: 2.1 (tree sources, units/CRS).
- Open: none.
- Owner approved PRs #1–#4; merged as one stack via #4 (main merged into
  `agent/claude-client`, stale open D8b dropped; 0.1, 0.2, 0.4, 1.1 → done).
## 2026-10-17 — Work: 0.4 Claude API client
- Done: `Core/Claude`: `ClaudeClient` (Messages API on plain HttpClient, no package), `ClaudeSettings`
  (key/model from %APPDATA%\CivilAI\settings.json, env overrides; key never printed), text + image
  content (type/size checks), structured JSON output via forced tool use (works with the Design
  Intent schema), retries with retry-after/backoff, clear errors. 18 new test cases with a fake handler.
  Branch `agent/claude-client` stacked on PR #3 (1.1) → #2 → #1.
- Blocked: NuGet still 403 (D8b open), so xUnit ran via an offline stand-in runner: 53/53 pass;
  Core builds clean with warnings-as-errors. CI proves the real run.
- Next: 1.2 prompt + validator (free text → Design Intent) on top of this client.
- Questions: none new. Default model `claude-sonnet-4-5` is a setting the owner can change.

## 2026-10-16 — Work: 1.1 Design Intent schema v0
- Done: `Core/Intent`: model (site/buildings/entrances with FFL, spaces plaza/path/lawn/bed,
  levels as elevation + spot levels + fall, edges flush/kerb/steps/wall, materials), JSON
  read/write (camelCase, strict shape), content validator (unique ids, references, closed
  outlines with area, path centerline + width, step/wall heights), JSON Schema v0 embedded
  for Claude structured output, sample `samples/intent/courtyard.v0.json` (D6). 19 new test cases.
  Branch `agent/design-intent-schema` is stacked on PR #2 (0.2), which is stacked on #1.
- Blocked: NuGet still 403 in the cloud (D8b), so xUnit ran via an offline stand-in runner:
  35/35 pass (Slope + Intent); Core builds clean with warnings-as-errors. CI proves the real run.
- Next: merge #1, #2, then this; next item 0.4 Claude API client (or 1.2 prompt, which needs 0.4).
- Questions: none new (D8b still open).

## 2026-10-16 — Work: 0.2 CI
- Done: `.github/workflows/ci.yml` (ubuntu, .NET 8: restore, build Release, test, upload
  TRX) on every PR and on push to main. Branch `agent/ci` is stacked on PR #1 (0.1) with
  main merged in; the docs conflicts from Meeting 1 are resolved there (stale open D8 removed).
- Blocked: NuGet still refused in the cloud (proxy 403) despite D8; .NET SDK came from
  Ubuntu apt. Core builds clean offline; xUnit tests and plugin shell are proven by CI only.
- Next: merge #1 then this PR; then 1.1 Design Intent schema v0.
- Questions: D8b (network access not active).

## 2026-10-16 — Meeting 1
- Decided: D1 (Civil 3D 2027), D2 (own API key to start), D4 (DWG survey), D8 (allow NuGet).
- Unblocked: 0.3, 0.4, 1.2, 1.3, 2.1. New: 5.4 MCP connector (blocked:D9).
- Also decided: D6 residential courtyard; 4.4 unblocked.
- Also decided: D9 build both modes (palette + connector), Core first; 5.4 unblocked.
- Still open: D3, D4b, D5, D7.
- PR #1 (0.1 skeleton) awaiting review; it adds D8 under Open, so drop that on merge (D8 now Decided on main).

## 2026-10-16 — Work: 0.1 solution skeleton
- Done: `CivilAI.sln` with `src/Core` (net8.0, no Autodesk refs), `src/Civil3DPlugin`
  (placeholder shell, net8.0-windows, refs Core; real target/refs at 0.3 per D1) and
  `tests/Core.Tests` (xUnit). First Core type: `Geometry.Slope` (percent ⇄ 1:n, between
  two levels, parse "5%" / "1:15", within-limit check) with 16 test cases. PR on
  `agent/solution-skeleton`.
- Blocked: NuGet is blocked in the cloud sandbox, so `dotnet test` and the plugin build
  could not run here; Core built clean and all tests passed via an offline stand-in runner.
- Next: 0.2 GitHub Actions build + test (will also prove the xUnit suite and plugin shell build).
- Questions: D8 (allow NuGet in cloud sessions); TFM note added to D1.

## 2026-10-16 — Setup (refocused on landscape architects)
- Done: operating manual, roadmap (M0–M5), architecture sketch, open decisions D1–D6.
- Next work item: 0.1 solution skeleton (not blocked), then 0.2 CI, then 1.1 schema.
- Questions: D1–D7 for the first meeting (template/standards, survey formats, plant library, first project type).
