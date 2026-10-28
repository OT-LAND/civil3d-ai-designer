# Architecture (initial)

> Concept v1 (D14, 2026-10-25): the designer's input is a host-neutral **Design Brief**
> (tagged polygons/curves/points + character sliders + style + background) → staged generation
> (Claude for style/plan/detail, Core for levels/metrics/rules) → Design Intent → host builders.
> See `docs/CONCEPT.md` §3. This file is updated as MB items land.

```
Designer inputs (sketch, image, text)      Background (survey, lots, rules)
              \                                   /
               v                                 v
        +---------------- Core (pure .NET, unit-tested) ----------------+
        |  Input readers -> Claude client -> Design Intent (JSON schema)  |
        |  Rule engine (accessibility, slopes, tree zones, falls)         |
        |  Design rule sets    (loaded from template/standard files)      |
        +-------------------------------+--------------------------------+
                                        | validated Design Intent
                                        v
        +------------- Civil3DPlugin (AutoCAD + Civil 3D .NET API) ----------+
        |  Builders: surfaces, feature lines, grading, hardscape, walls,      |
        |            planting, drainage, site elements, labels, schedules    |
        |  Palette UI: inputs, preview, constraint report, edit-by-instruction |
        |  Template manager, generation history (undo groups per step)        |
        +----------------------------------------------------------------------+
```

Design Intent v1 (1.1, extended by B.3)
- Metres, drawing coordinates (CRS per D4b), absolute levels. Polygons implicitly closed.
- site (lot border, buildings with FFL and entrances) · spaces (plaza/lawn/bed by outline,
  path by centerline + width; level = nominal elevation, spot levels, fall) · edges (flush,
  kerb, steps, wall between 1–2 spaces, with height) · materials (id, name, category).
- v1 (B.3, all optional so v0 files still read; "0" files must not use them): spaces carry
  `zoneId` / `interestPointId` (Design Brief refs, not checked inside the intent) · trees (new or
  existing, canopy diameter, trunk, protection radius for existing, `sourceId` = survey id (required
  for existing) or brief point) · drainage (swales high→low with `outletId` → basin or low point,
  retention/infiltration basins, low points with `inlet`) · elements (seating, lighting, play, shade,
  pergola, water, art, other; shade/pergola need a footprint for canopy cover). Schema
  `design-intent.v1.schema.json`; new intents default to "1".
- Two checks: `DesignIntentJson.TryParse` (shape: unknown fields/kinds rejected) and
  `DesignIntentValidator` (ids, references, usable geometry). Design rules are 2.2, not here.

Claude client (0.4, palette mode of D9)
- `Core/Claude`: `ClaudeClient` posts to the Messages API with plain HttpClient + System.Text.Json
  (no SDK package). Settings: `%APPDATA%\CivilAI\settings.json` (`anthropicApiKey`, `model`,
  `maxTokens`, `maxRetries`, `baseUrl`), overridden by env `ANTHROPIC_API_KEY` / `CIVILAI_CLAUDE_MODEL` (D2).
- Content: text + images (JPEG/PNG/GIF/WebP, base64, 5 MB each) for sketches and references.
- Structured output by forced tool use: `JsonOutputSpec(toolName, schema)` sends the schema as the
  tool's `input_schema` with `tool_choice` = that tool; the reply's tool input is `ClaudeResponse.Json`.
  The Design Intent schema (`DesignIntentJson.ReadSchema()`) plugs in directly (1.2).
- Retries 408/409/429/5xx/529 and transport errors with server `retry-after` or 1-2-4 s backoff +
  jitter; 400/401/403/404 fail at once with the API's message (`ClaudeApiException`).

Free text → Design Intent (1.2)
- `Core/Intent/TextToIntent`: brief + optional surveyed `Site` → `TextToIntentResult` (intent, assumptions,
  attempts, tokens). System prompt is the embedded `Intent/Prompts/text-to-intent.v0.md` (landscape language,
  units, site rules, default falls/widths stated as assumptions).
- Tool output = `{ assumptions: string[], intent: Design Intent v0 }` (intent schema's `$defs` lifted to the
  root). Each answer goes through `TryParse` + `DesignIntentValidator`; problems are sent back in a repair
  prompt (brief + previous JSON + problem list), up to `MaxAttempts` (3) calls, then `TextToIntentException`.
- The given site always replaces Claude's site: the AI cannot move buildings or change FFLs.
- Takes a send delegate, so the connector mode (D9) or tests can supply their own sender.

Design Brief v1 (B.1, concept v1)
- `Core/Brief/DesignBrief`: the generation input captured from the palette: `background` refs (survey,
  parcel, structures, roads, utilities, orthophoto (D21); host refs like `layer:V-TOPO-*`), `style` (prompt + up to 6 images),
  global `character` sliders (all 9 required, 0–100; `budget` = cost level 0 economical – 100 premium, D20), `zones` (outline + per-zone slider overrides, D15),
  tagged `curves` (walkingNetwork/contour/flowBasin/border) and `points` (entrance/interestPoint/elevation/tree).
  Every item may carry `hostId` (Civil 3D: entity handle) so the brief can be re-read from the drawing.
- Metres only (`units: "m"`); capture converts drawing units. Points are `{x, y}` like the Design Intent;
  levels are a separate `level` field (contour → hard contour, D17; elevation point needs one; entrance optional).
- `DesignBrief.CharacterFor(zone)` resolves the 8 sliders for a zone (own value, else global).
- `DesignBriefJson` (same serializer options as the intent; unknown fields/tags rejected) + embedded schema
  `Brief/Schema/design-brief.v1.schema.json`; `DesignBriefValidator` checks ids, geometry (shared helpers
  from `DesignIntentValidator`), slider ranges, a complete global set, and tag-only fields.

Metrics on a Design Intent (B.2)
- `Core/Metrics/IntentMetricsCalculator.Measure(intent, zoneOutline?)` → `IntentMetrics`: hard/soft area and
  soft share (kind lawn/bed = soft, unless the material category says paving/gravel or lawn/planting/mulch),
  curved/straight length share of outlines + path centerlines (a segment is curved when both ends are smooth
  bends, turn 0.5°–30°), accessible share of path length (D11), steepest slope per surface (accessible path,
  not-accessible path, plaza, lawn/bed) from falls and spot-level pairs. With a zone outline only spaces whose
  centre lies inside are measured. v0 limits: overlaps not subtracted; path area = length × width.
  B.3a adds canopy cover (tree canopy circles + shade/pergola footprints, overlaps once, over the zone outline
  or else the lot boundary, buildings left out; `CoverArea` grid sampling, ~250k cells) and the elements share
  of new trees + site elements by count (location in the zone; kept trees not counted).
- `TargetComparison.Compare(metrics, brief.CharacterFor(zone), tolerances?)` → one `TargetResult` per slider
  (Met / Missed / NotMeasured + note). Tolerances: hard/soft ±5 (concept), curved/straight ±10, accessibility
  is a minimum (−5), shading ±10, trees/elements ±10. Max grade, cut/fill, max slope wait for B.6.
- `Core/Geometry/PolygonGeometry`: area, length, point-in-polygon, half-length point, curved/straight split.

Style profile (B.4, CONCEPT §3.3 stage 2)
- `Core/Style/StyleProfile`: summary, material palette (≤12, id/name/category/colour/finish/use; `ToIntentMaterials()`
  gives Design Intent materials), planting character (water use, density, layers, palette, example genera —
  suggestions only, D5), form language (orthogonal/angular/curvilinear/mixed, curved share 0–100 on the
  curved↔straight scale, edges, level changes), one note per reference image. `StyleProfileJson` +
  embedded `style-profile.v1.schema.json`; `StyleProfileValidator`.
- `StyleReader.ReadAsync(StyleRequest)`: labelled images ("Reference image n: name") then the prompt, system
  prompt `Style/Prompts/style-reader.v1.md`, forced JSON output, repair loop (default 2 calls).
  `StyleRequest.FromInput(brief.Style, folder)` reads the brief's image files.
- Cache: `StyleProfileCache(folder)` keeps `<sha256>.json` per input set; key = prompt version + trimmed
  prompt + image types/bytes in order (file names ignored). Bad entries are misses. The plugin chooses the folder.

Plan stage (B.5a/B.5b, CONCEPT §3.3 stage 3)
- `Core/Plan/PlanStage.GenerateAsync(PlanRequest {Brief, Style?, Site?, Rules?})` → one Design Intent. User
  message: resolved sliders per zone in plain words, then brief, style profile and site as JSON; system prompt
  `Plan/Prompts/plan.v3.md` (v2: budget in words via `PlanStage.BudgetWords`, utilities keep-clear; v3: layout
  options, `concept` line); forced output = 1.2 shape plus a required `concept` string (`PlanStage.BuildOutputSchema`),
  answer read as 1.2 (`TextToIntent.TryReadAnswer`, given site swapped in); a missing concept never triggers a repair. Default 3 calls, 16k output tokens per call.
- `PlanStage.Review(intent, brief, rules)` → `PlanReview`: **problems** (space zoneId not a brief zone or missing,
  interestPointId unknown, interest point with no place or several) block the plan; **open issues** (rule
  failures, slider misses per zone with "raise/lower it") are repaired but not blocking. After the last call
  the usable plan with the fewest open issues is returned with them (`AllTargetsMet` false); no usable plan ⇒
  `PlanStageException`.
- `PlanStage.GenerateOptionsAsync(request, count = 3)` → `PlanOptions` (D18): options planned one after another,
  each prompt (`BuildOptionPrompt`) lists the concepts of the usable options so far and asks for a clearly
  different layout; each option is reviewed/repaired as above (up to `MaxAttempts` calls each, so ≤ 9 calls).
  An option that never becomes usable goes to `Failed` and the others are returned; none usable ⇒
  `PlanStageException`. Token totals cover the whole run. Only the chosen option is detailed later (B.6+).

Existing-tree table import (2.1a)
- `Core/Survey/CsvTable` reads CSV (separator detected from the header line, quoted cells, BOM).
- `TreeFieldMapping.Suggest(headers)` proposes which column is id/species/X/Y/trunk/canopy/height/level
  (English + Hebrew aliases, unit suffix ignored); the designer confirms it. `TreeTableUnits.Suggest` takes
  units only from headings; the coordinate system is named by the designer.
- `TreeTableImport.Import(table, mapping, units)` → `TreeImportResult {Questions, Trees, Problems}`. Any
  question ⇒ no trees. Sizes converted to metres; `ExistingTree` is Core's metre-based tree record (2.1b
  DWG blocks map into the same record).
- `Core/Survey/XlsxTable.Read(stream, sheet?)` (2.1c, D12) reads an .xlsx sheet into the same `CsvTable`
  (package ExcelDataReader, MIT). Several visible sheets with content and none chosen ⇒ a question, no table.
  Excel row numbers are kept for messages; numbers come from cell values (no decimal-comma guessing).
  Trunk is always a diameter (D12).

Rule engine (2.2a)
- `Core/Rules/RuleSet`: a rule-set file = `{ruleSetVersion, id, name, description, rules[{id, value, label,
  source, verified}]}`. Ids are a closed list (`RuleIds`); unknown/duplicate ids, negative values or another
  version are rejected with every problem listed. A rule left out of a set = "not checked" (office sets may omit).
  Default starter set `Rules/RuleSets/default-il.v0.json` is embedded (`RuleSetJson.LoadDefault()`, D3).
- `RuleEngine.Check(intent, ruleSet)` → `RuleReport` of `RuleResult {ruleId, spaceId, Pass|Fail|NotChecked,
  measured, limit, location, message}`, one per rule per space, worst place found = zoom target (for 2.3).
- Paths: slope along the path from spot levels ordered by station on the centerline (else the fall's along
  component); cross fall = the fall's component across each centerline segment. Plaza/lawn min fall needs a
  stated fall (spots only bound it); max fall/slope uses the fall and the steepest pair of spot levels.
- Accessible route (D11): every path is one unless the intent marks it `"accessible": false` (path-only
  field); such a path gets NotChecked for the two accessible-route rules. Default set values are
  owner-confirmed (D10, `verified: true`).

Principles
- Claude produces intent; deterministic code produces geometry.
- Every generation step is previewable, reversible, and re-runnable.
- `Core` has zero Autodesk references so it builds and tests in the cloud.

Repository layout
```
CivilAI.sln
src/Core/            CivilAI.Core         net8.0, no Autodesk refs (Geometry/, Intent/, later Rules/, Claude/)
src/Core/Intent/     Design Intent v1 model, JSON read/write, content validator; Schema/ holds the
                     JSON Schema (embedded resource, for Claude structured output in 0.4/1.2)
samples/intent/      Sample Design Intents (courtyard.v0.json = D6 residential courtyard; courtyard.v1.json
                     = the B.1 brief's courtyard with trees, drainage, elements); used by tests
src/Core/Intent/Prompts/  System prompts (embedded), versioned by file name
src/Core/Brief/      Design Brief v1 model, JSON read/write, validator; Schema/ holds the JSON Schema (B.1)
samples/brief/       Sample Design Brief (courtyard.brief.v1.json); used by tests
src/Core/Metrics/    Metrics on a Design Intent and target-vs-achieved against brief sliders (B.2)
src/Core/Style/      Style profile (B.4): model, JSON, validator, StyleReader (Claude vision) + file cache; Schema/, Prompts/
samples/style/       Sample style profile (courtyard.style.v1.json, matches the B.1 brief's style); used by tests
src/Core/Survey/     Survey import: CSV and .xlsx table readers, existing-tree table import (2.1a, 2.1c)
samples/survey/      Sample survey data (trees-courtyard.csv/.xlsx, trees-hebrew.xlsx)
src/Core/Rules/      Rule sets (RuleSets/*.json embedded, default-il.v0 = starter set) and RuleEngine
src/Core/Claude/     Claude Messages API client, settings (key from settings/env), image input, JSON output
src/Civil3DPlugin/   CivilAI.Civil3DPlugin  Civil 3D plugin; placeholder until 0.3 (TFM + Autodesk refs per D1)
tests/Core.Tests/    CivilAI.Core.Tests   xUnit tests for Core
.github/workflows/ci.yml  CI: restore, build, test CivilAI.sln on ubuntu (every PR + main)
```
