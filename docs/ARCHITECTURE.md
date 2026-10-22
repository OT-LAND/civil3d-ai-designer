# Architecture (initial)

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

Design Intent v0 (1.1)
- Metres, drawing coordinates (CRS per D4b), absolute levels. Polygons implicitly closed.
- site (lot border, buildings with FFL and entrances) · spaces (plaza/lawn/bed by outline,
  path by centerline + width; level = nominal elevation, spot levels, fall) · edges (flush,
  kerb, steps, wall between 1–2 spaces, with height) · materials (id, name, category).
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

Existing-tree table import (2.1a)
- `Core/Survey/CsvTable` reads CSV (separator detected from the header line, quoted cells, BOM).
- `TreeFieldMapping.Suggest(headers)` proposes which column is id/species/X/Y/trunk/canopy/height/level
  (English + Hebrew aliases, unit suffix ignored); the designer confirms it. `TreeTableUnits.Suggest` takes
  units only from headings; the coordinate system is named by the designer.
- `TreeTableImport.Import(table, mapping, units)` → `TreeImportResult {Questions, Trees, Problems}`. Any
  question ⇒ no trees. Sizes converted to metres; `ExistingTree` is Core's metre-based tree record (2.1b
  DWG blocks map into the same record).

Principles
- Claude produces intent; deterministic code produces geometry.
- Every generation step is previewable, reversible, and re-runnable.
- `Core` has zero Autodesk references so it builds and tests in the cloud.

Repository layout
```
CivilAI.sln
src/Core/            CivilAI.Core         net8.0, no Autodesk refs (Geometry/, Intent/, later Rules/, Claude/)
src/Core/Intent/     Design Intent v0 model, JSON read/write, content validator; Schema/ holds the
                     JSON Schema (embedded resource, for Claude structured output in 0.4/1.2)
samples/intent/      Sample Design Intents (courtyard.v0.json = D6 residential courtyard); used by tests
src/Core/Intent/Prompts/  System prompts (embedded), versioned by file name
src/Core/Survey/     Survey import: CSV table reader, existing-tree table import (2.1a)
samples/survey/      Sample survey data (trees-courtyard.csv)
src/Core/Claude/     Claude Messages API client, settings (key from settings/env), image input, JSON output
src/Civil3DPlugin/   CivilAI.Civil3DPlugin  Civil 3D plugin; placeholder until 0.3 (TFM + Autodesk refs per D1)
tests/Core.Tests/    CivilAI.Core.Tests   xUnit tests for Core
.github/workflows/ci.yml  CI: restore, build, test CivilAI.sln on ubuntu (every PR + main)
```
