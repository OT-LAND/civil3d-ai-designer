# Decisions

## Open (for the next meeting)

_None._

## Decided

**D10 — Default accessibility values confirmed** (2026-10-23)
**(a) OK as proposed**: accessible route max **5%** along the path (steeper = ramp),
max cross fall **2%**, paved plaza fall **1–2%**, lawn fall min **1%** / max **25% (1:4)**,
planting bed max **33% (1:3)**. Mark these rules `verified: true` in
`default-il.v0.json` (source note: confirmed by owner 2026-10-23).

**D11 — All paths are accessible routes unless marked otherwise** (2026-10-23)
**(a)**: every path is checked against accessible-route limits by default; the designer
can mark a path "not accessible" (new flag in the Design Intent), and it is then
skipped by the accessible-route rules (other slope rules still apply).

**D12 — Tree tables: open Excel directly; trunk is diameter** (2026-10-23)
(1) **(a)**: the plugin reads **.xlsx** tree tables directly (one small open-source
reader package), in addition to CSV. (2) **(a)**: trunk is always given as
**diameter** (at 1.3 m); no circumference option needed.

**D8b — Cloud network access is active** (2026-10-18)
**(a)**: the owner has added the NuGet / .NET hosts to network access. Work sessions
run the real `dotnet restore` / `dotnet test` before pushing; the offline stand-in
runner is no longer needed. (If NuGet is still refused, log it and fall back to CI.)

**D3 — Templates and design standards: user chooses** (2026-10-18)
There is **no office template yet**. The designer must be able to **choose a template**
(.dwt with layers, styles, labels) and a **design-rule set** per drawing/project.
The plugin ships a **default starter template and a default rule set** (published
Israeli accessibility values: route and ramp slopes, landings, handrails; plus generic
lawn/bed slope and drainage-fall limits), all in editable data files with their source
noted. Office- or municipality-specific rule sets are added later as extra files.

**D4b — Trees and coordinates in the survey** (2026-10-18)
**(c) Support both**: trees as attributed blocks in the survey DWG, and trees as
points/blocks with species and sizes in a separate table (Excel/CSV), with a mapping
step for field names. **Coordinate system and units vary** by project: the import must
read the drawing's units and coordinate system when set, otherwise ask the designer
(never assume ITM or metres silently). Core works in metres internally.

**D5 — Planting: AI proposes species from the user's plant list** (2026-10-18)
**(b)**: the AI proposes planting zones, tree positions **and species**, matched to
sun, water and space, chosen **only from a plant list the user selects or uploads**
(same pattern as D3: a default starter list ships, users choose/upload their own).
Plant list fields: species, size at planting, mature canopy, water needs, spacing.

**D7 — Agent testing in Civil 3D** (2026-10-18)
**(c)**: the agent tests in the owner's Civil 3D only when asked during a meeting.
Work sessions stay cloud-only; the owner tests "Please test" PRs. Revisit (b) once
the plugin loads reliably.

**D1 — Civil 3D version** (2026-10-16)
Civil 3D **2027** only. Plugin targets the 2027 .NET API (.NET 8). The owner tests
"Please test" PRs.

**D2 — How the plugin talks to Claude** (2026-10-16)
Start with **(a): each user's own Anthropic API key**, read from user settings/env.
Product direction: it should feel like a Claude plugin: the user works with their own
Claude account, the plugin is sold on a monthly/annual plan (no token billing), and it
is also usable as a tool for agentic work. Follow-up in D9.

**D4 — Survey format** (2026-10-16)
The survey usually arrives as a **DWG**. Build the survey import for DWG first. Trees
and coordinates: follow-up in D4b.

**D6 — First target project type** (2026-10-16)
**(a) Residential courtyard / building surroundings.** First end-to-end loop: lot
boundary, building FFL tie-ins, accessible route from the street.

**D9 — Two ways to work, user's choice** (2026-10-16)
**Build both, core first.** Order: shared Core (grading, rules, Design Intent,
builders) first; then the user chooses per session how to work:
(1) Civil 3D palette calling Claude with the user's own API key, or
(2) a connector (MCP server) so Claude, on the user's own Claude account, drives the
same Core tools, including agentic work. Neither mode may need logic the other lacks.

**D8 — Cloud sessions may download .NET packages** (2026-10-16)
**(a)**: the owner allows `api.nuget.org`, `dot.net`, `builds.dotnet.microsoft.com` in
network access. Work sessions run the real `dotnet test`.

