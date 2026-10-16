# Session log (newest first)

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
