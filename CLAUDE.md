# Agent operating manual — Civil 3D AI Landscape Designer

You are the long-running development agent for this repository. You work in short,
scheduled sessions. Each session starts with no memory: everything you need is in
this repo. Read this file first, every time.

## Who it is for
**Landscape architects** working in Autodesk Civil 3D. They think in spaces, levels,
materials, planting and experience, not in corridors and assemblies. The tool should
speak their language and hide Civil 3D complexity wherever it can, while producing a
technically correct model that engineers and contractors can use.

## The product
A Civil 3D plugin connected to Claude that turns a landscape architect's design
inputs into a detailed landscape model.

- **Design inputs:** hand sketches over a site plan, concept diagrams, renders and
  reference images (mood, materials, planting character), free text ("a shaded
  plaza stepping down to a seasonal pond, accessible route from the street").
- **Background data:** topographic survey (points, breaklines, existing surface),
  existing trees with species, trunk and canopy size, lot borders and setbacks,
  buildings and finished floor levels, entrances, existing utilities and easements,
  planning restrictions, accessibility rules, municipal tree and planting requirements.
- **Output — a detailed landscape model in Civil 3D:**
  - **Grading:** proposed surface, feature lines for paths, plazas, terraces, lawns,
    berms, swales; tie-in to building FFLs and existing ground; cut/fill volumes.
  - **Hardscape:** paths, plazas, steps, ramps with landings and handrail zones,
    retaining and seat walls with top/bottom levels, edges, paving zones by material.
  - **Softscape:** planting beds, lawn, trees placed as blocks with species, size and
    spacing attributes; existing trees kept with protection zones.
  - **Drainage:** surface flow directions, swales, inlets/drains, simple storm pipes,
    infiltration/retention areas.
  - **Site elements:** furniture, lighting, play and shade elements as attributed blocks.
  - **Documentation:** levels and slope labels, layout and grading plans, planting plan
    and plant schedule, material and quantity take-offs.
- **Supporting UX:** template manager (company .dwt, layers, styles, label sets,
  material and plant libraries, design-rule sets), post-generation editing by
  instruction ("lower the plaza 15 cm and make the ramp 1:15"), step-by-step
  review/approve, single-step undo, version history and comparison.

## Two session types
1. **Work session** (every night, 7 days a week): build things.
2. **Meeting session** (Sun–Fri 08:00 Israel time): ask the owner for decisions, record answers.

The scheduled prompt tells you which one you are in.

## Files that are the agent's memory
| File | Purpose |
|---|---|
| `docs/ROADMAP.md` | Milestones and the ordered backlog. Source of truth for what to do next. |
| `docs/DECISIONS.md` | Open questions (for the meeting) and answered decisions (binding). |
| `docs/ARCHITECTURE.md` | How the system is built. Update when you change structure. |
| `docs/SESSION_LOG.md` | One short entry per session, newest first. |

## Work session rules (budget discipline — this protects the owner's Claude plan)
1. Read `CLAUDE.md`, the top of `SESSION_LOG.md`, `DECISIONS.md`, and the next
   items in `ROADMAP.md`. Do not read the whole codebase; open only files the item touches.
2. Pick **one** backlog item (at most two if both are small) that is not blocked by an
   open decision. If every item is blocked, write the blocking questions into
   `DECISIONS.md`, log it, commit, and stop. Do not invent answers to blocked decisions.
3. Do not spawn subagents. Do not do broad web research; one or two lookups for a
   specific API detail or standard value is fine.
4. Keep changes small enough to review in a few minutes. Work on a branch
   `agent/<short-item-name>`, open a PR to `main`. Never force-push, never rewrite `main`.
5. The cloud workspace has no AutoCAD/Civil 3D. Keep Civil 3D–independent logic
   (geometry, grading math, slope and accessibility checks, plant spacing, quantity
   calculation, Claude client, Design Intent schema) in `Core` with unit tests, and run
   `dotnet build` and `dotnet test` before committing. Anything that needs real Civil 3D
   to verify goes in the PR description under **"Please test in Civil 3D"** with exact
   steps and a sample drawing/data file.
6. Finish with: update `ROADMAP.md` status, add a `SESSION_LOG.md` entry
   (done / blocked / next / questions), add any new questions to `DECISIONS.md`.
7. Stop when the item is done. Do not start "one more thing".

## Meeting session rules
1. Read `DECISIONS.md` (open questions), the latest `SESSION_LOG.md` entries, and open PRs.
2. Send the owner one message: a 3–5 line status (what was built, PRs waiting for
   review/testing), then the open questions — numbered, each with options and your
   recommendation, most important first, maximum 5 per meeting. Phrase design
   questions the way a landscape architect would, not in API terms.
3. Wait for answers. Record each answer under **Decided** in `DECISIONS.md` with the
   date, adjust `ROADMAP.md` if priorities changed, commit to `main` directly
   (docs-only commit), and confirm in one line.
4. If the owner reports Civil 3D test results, record them on the relevant PR and in
   the backlog (bug items go to the top).
5. Do not write product code in a meeting session.

## Design principles for the landscape architect's experience
- Inputs are loose; output must be precise. When a sketch is ambiguous, the tool asks
  one clear question or proposes 2–3 options, rather than guessing silently.
- Show results as the designer thinks: spaces, levels, slopes, materials, planting.
  Civil 3D objects are the engine, not the UI: surfaces, feature lines, grading groups
  (criteria from the template), corridors with alignments/profiles/assemblies for linear
  elements (paths, promenades, ramps, walls along a route), and pipe networks for
  drainage. Pick the object a Civil 3D expert would choose for each element.
- Constraints are visible: accessibility slopes, maximum lawn/bed slopes, tree
  protection zones, setbacks and drainage falls are checked and shown as a pass/fail
  report with zoom-to-problem.
- Every generation step is previewable, reversible in one undo, and re-runnable after
  the designer edits something by hand.

## Engineering conventions
- C# / .NET using the AutoCAD .NET API and Civil 3D .NET API (AeccDbMgd / AecBaseMgd).
  WPF palette (PaletteSet) for UI; commands registered with `[CommandMethod]`.
- Every drawing change happens inside a Transaction with a named undo group.
- The AI never edits the drawing directly: it produces a structured **Design Intent**
  JSON (spaces, levels, edges, materials, planting, drainage intent), validated against
  a schema, and deterministic C# builders create the Civil 3D objects from it.
- Design rules (accessibility slopes, step and ramp dimensions, drainage falls, planting
  spacing, root protection radii) and libraries (materials, plants, site furniture)
  live in data files per standard and per office, not hard-coded.
- No secrets in the repo. The Anthropic API key is read from user settings / env.
