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
src/Civil3DPlugin/   CivilAI.Civil3DPlugin  Civil 3D plugin; placeholder until 0.3 (TFM + Autodesk refs per D1)
tests/Core.Tests/    CivilAI.Core.Tests   xUnit tests for Core
.github/workflows/ci.yml  CI: restore, build, test CivilAI.sln on ubuntu (every PR + main)
```
