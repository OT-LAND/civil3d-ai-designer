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

Principles
- Claude produces intent; deterministic code produces geometry.
- Every generation step is previewable, reversible, and re-runnable.
- `Core` has zero Autodesk references so it builds and tests in the cloud.

Repository layout
```
CivilAI.sln
src/Core/            CivilAI.Core         net8.0, no Autodesk refs (Geometry/, later Intent/, Rules/, Claude/)
src/Civil3DPlugin/   CivilAI.Civil3DPlugin  Civil 3D plugin; placeholder until 0.3 (TFM + Autodesk refs per D1)
tests/Core.Tests/    CivilAI.Core.Tests   xUnit tests for Core
```
