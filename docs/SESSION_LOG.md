# Session log (newest first)

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
