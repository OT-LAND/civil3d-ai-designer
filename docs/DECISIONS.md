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

**D6 — First target project type**
Pick the first project type to make work end-to-end: (a) residential courtyard /
building surroundings, (b) urban plaza or pocket park, (c) neighbourhood park.
*Recommendation:* (a) — clear boundaries, FFL tie-ins, accessibility rules; a strong
first loop.

**D7 — Let the agent test in your Civil 3D?**
Your computer has a Civil 3D connection the agent can drive (run commands, query the
drawing). If work sessions may use it, the agent can load each build and check results
itself, but only when your PC is on with the Claude desktop app open, and it uses more
of your plan. Options: (a) cloud only, you test; (b) agent tests in Civil 3D on a
sample drawing during the work session; (c) agent tests only when you ask in a meeting.
*Recommendation:* (c) to start, move to (b) once the plugin loads reliably.

**D9 — Business model: run on the designer's own Claude account (follow-up to D2)**
Long-term goal: designers use their own Claude subscription, and the plugin is sold
monthly/annually with no token billing, and it can also be driven by Claude for agentic
work. A plugin calling Claude from inside Civil 3D needs an API key (pay per use);
using the designer's own Claude plan means Claude drives Civil 3D through a connector
(MCP server) from the Claude app instead. Options: (a) build both — the in-Civil 3D
palette on an API key now, and an MCP connector exposing the same Core tools; (b) go
connector-first and make the palette a thin front-end.
*Recommendation:* (a) — Core stays the same for both; add the connector after the
first end-to-end loop works.

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

**D8 — Cloud sessions may download .NET packages** (2026-10-16)
**(a)**: the owner allows `api.nuget.org`, `dot.net`, `builds.dotnet.microsoft.com` in
network access. Work sessions run the real `dotnet test`.

