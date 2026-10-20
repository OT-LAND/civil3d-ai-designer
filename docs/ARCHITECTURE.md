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

Plugin shell (0.3)
- `src/Civil3DPlugin` targets Civil 3D 2027 (D1, release R26.0) on **net10.0-windows**: the 2027 API is
  .NET 10 (Autodesk NuGet AutoCAD.NET 26.0.0 targets net10.0); Core stays net8.0 and loads in that host. With Civil 3D installed (Windows,
  `$(AutoCADDir)` = `C:\Program Files\Autodesk\AutoCAD 2027`) it references accoremgd/acdbmgd/acmgd/AdWindows/
  AeccDbMgd (Private=false), defines CIVIL3D_API and writes `bin/<cfg>/<tfm>/CivilAI.bundle` (autoloader;
  `-p:InstallBundle=true` copies it to `%APPDATA%\Autodesk\ApplicationPlugins`). Without Civil 3D (Linux/CI)
  `Civil3D/` still compiles against the compile-only NuGet AutoCAD.NET 26.0.0 (never copied to output);
  only the AeccDbMgd call sits behind `#if CIVIL3D_API`; no bundle is made. CI installs .NET 8 + 10 SDKs.
- `PluginApp` (IExtensionApplication): adds the "AI Landscape" ribbon tab when the ribbon is ready and after
  a workspace switch (WSCURRENT). Commands (group CIVILAI): `AIHELLO` (status text from Core
  `Diagnostics/HelloReport`), `AIPALETTE` (PaletteSet hosting WPF, fixed Guid; status panel for now).

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
src/Core/Claude/     Claude Messages API client, settings (key from settings/env), image input, JSON output
src/Core/Diagnostics/ HelloReport: AIHELLO / palette status text (versions, Civil 3D, Claude key; never the key)
src/Civil3DPlugin/   CivilAI.Civil3DPlugin  Civil 3D 2027 plugin (net10.0-windows); Civil3D/ (Autodesk code),
                     Bundle/PackageContents.xml (autoloader manifest)
tests/Core.Tests/    CivilAI.Core.Tests   xUnit tests for Core
.github/workflows/ci.yml  CI: restore, build, test CivilAI.sln on ubuntu (every PR + main)
```
