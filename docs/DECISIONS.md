# Decisions

## Open (for the next meeting)

**D3 — Office template and design standards**
Is there an existing office .dwt (layers, styles, labels)? Which rules apply: Israeli
accessibility regulations (route and ramp slopes, landings, handrails), municipal
landscape and tree requirements, water-wise planting guidelines?
*Recommendation:* send one real template and the key rule tables; the agent turns them
into rule files.

**D4b — Tree survey and coordinates (follow-up to D4)**
The survey arrives as a DWG. Do existing trees come as blocks in that DWG with
attributes (species, trunk, canopy), or as a separate table? Is the coordinate system
always the Israeli TM Grid (ITM)?
*Recommendation:* tree blocks in the DWG plus an optional CSV table; ITM.

**D5 — Plant library**
Is there an office plant list (species, size at planting, mature canopy, water needs,
spacing)? Should the AI propose species or only planting zones the designer fills in?
*Recommendation:* v1 proposes zones and tree positions; species from your office list.

**D7 — Let the agent test in your Civil 3D?**
Your computer has a Civil 3D connection the agent can drive (run commands, query the
drawing). If work sessions may use it, the agent can load each build and check results
itself, but only when your PC is on with the Claude desktop app open, and it uses more
of your plan. Options: (a) cloud only, you test; (b) agent tests in Civil 3D on a
sample drawing during the work session; (c) agent tests only when you ask in a meeting.
*Recommendation:* (c) to start, move to (b) once the plugin loads reliably.

**D8b — Network access for cloud sessions is not active yet (follow-up to D8)**
On 2026-10-16 the cloud session was still refused `api.nuget.org` and `dot.net` (proxy
403). The .NET SDK was installed from Ubuntu packages instead, but NuGet restore fails,
so the agent cannot run `dotnet test` itself; CI (0.2) runs it on GitHub instead.
Options: (a) add `api.nuget.org` (and optionally `dot.net`, `builds.dotnet.microsoft.com`)
under claude.ai → Settings → Capabilities → network access; (b) rely on CI only.
*Recommendation:* (a); a 2-minute settings change, and the agent can then prove tests
pass before pushing.

## Decided

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

