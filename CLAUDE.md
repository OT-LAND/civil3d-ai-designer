# Agent operating manual — Civil 3D AI Designer

You are the long-running development agent for this repository. You work in short,
scheduled sessions. Each session starts with no memory: everything you need is in
this repo. Read this file first, every time.

## The product
An Autodesk Civil 3D plugin connected to Claude that turns a designer's inputs into a
detailed Civil 3D model.

- **Design inputs:** hand sketches, diagrams, renders, images, free text.
- **Background data:** survey (points, breaklines, existing surface), lot/parcel borders,
  easements, setbacks, zoning and authority restrictions, existing utilities.
- **Output:** a full Civil 3D model built from the inputs and constrained by the
  background — existing and proposed surfaces, parcels, alignments, profiles,
  assemblies and corridors, grading and feature lines, pipe and pressure networks,
  plus the sheets/labels the template defines.
- **Supporting UX:** template manager (.dwt, styles, label sets, standard assemblies,
  parts lists), post-generation editing ("lower the road profile 0.3 m at the
  junction"), step-by-step review/approve, undo, version history.

## Two session types
1. **Work session** (Sun–Thu night): build things.
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
   specific API detail is fine.
4. Keep changes small enough to review in a few minutes. Work on a branch
   `agent/<short-item-name>`, open a PR to `main`. Never force-push, never rewrite `main`.
5. The cloud workspace has no AutoCAD/Civil 3D. Keep Civil 3D–independent logic
   (geometry, design rules, constraint checks, Claude client, Design Intent schema) in
   `Core` with unit tests, and run `dotnet build` and `dotnet test` before committing.
   Anything that needs real Civil 3D to verify goes in the PR description under
   **"Please test in Civil 3D"** with exact steps and a sample drawing/data file.
6. Finish with: update `ROADMAP.md` status, add a `SESSION_LOG.md` entry
   (done / blocked / next / questions), add any new questions to `DECISIONS.md`.
7. Stop when the item is done. Do not start "one more thing".

## Meeting session rules
1. Read `DECISIONS.md` (open questions), the latest `SESSION_LOG.md` entries, and open PRs.
2. Send the owner one message: a 3–5 line status (what was built, PRs waiting for
   review/testing), then the open questions — numbered, each with options and your
   recommendation, most important first, maximum 5 per meeting.
3. Wait for answers. Record each answer under **Decided** in `DECISIONS.md` with the
   date, adjust `ROADMAP.md` if priorities changed, commit to `main` directly
   (docs-only commit), and confirm in one line.
4. If the owner reports Civil 3D test results, record them on the relevant PR and in
   the backlog (bug items go to the top).
5. Do not write product code in a meeting session.

## Engineering conventions
- C# / .NET using the AutoCAD .NET API and Civil 3D .NET API (AeccDbMgd / AecBaseMgd).
  WPF palette (PaletteSet) for UI; commands registered with `[CommandMethod]`.
- Every drawing change happens inside a Transaction with a named undo group so the
  designer can undo a generation step in one action.
- The AI never edits the drawing directly: it produces a structured **Design Intent**
  JSON (validated against a schema), and deterministic C# builders create the Civil 3D
  objects from it. This keeps generation testable, repeatable and reviewable.
- Design rules (slopes, radii, cover depths, setbacks) live in data files per standard,
  not hard-coded, so the template manager can swap them.
- No secrets in the repo. The Anthropic API key is read from user settings / env.
