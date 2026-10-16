# Architecture (initial)

```
Designer inputs (sketch, image, text)      Background (survey, lots, rules)
              \                                   /
               v                                 v
        +---------------- Core (pure .NET, unit-tested) ----------------+
        |  Input readers -> Claude client -> Design Intent (JSON schema)  |
        |  Constraint engine  (setbacks, slopes, cover, levels)           |
        |  Design rule sets    (loaded from template/standard files)      |
        +-------------------------------+--------------------------------+
                                        | validated Design Intent
                                        v
        +------------- Civil3DPlugin (AutoCAD + Civil 3D .NET API) ----------+
        |  Builders: surfaces, parcels, alignments, profiles, assemblies,     |
        |            corridors, grading, pipe networks, labels                |
        |  Palette UI: inputs, preview, constraint report, edit-by-instruction |
        |  Template manager, generation history (undo groups per step)        |
        +----------------------------------------------------------------------+
```

Principles
- Claude produces intent; deterministic code produces geometry.
- Every generation step is previewable, reversible, and re-runnable.
- `Core` has zero Autodesk references so it builds and tests in the cloud.
