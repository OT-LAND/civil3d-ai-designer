# Decisions

## Open (for the next meeting)

**D1 — Civil 3D version and test setup**
Which Civil 3D release(s) must be supported (e.g. 2025, 2026, 2027)? This sets the .NET
target and API surface. Who tests in Civil 3D, and how often?
*Recommendation:* your current release only; you test each "Please test" PR when
convenient and report in the meeting.

**D2 — How the plugin talks to Claude**
The plugin calls the Claude API from inside Civil 3D. That usage is billed to an
Anthropic API account (console.anthropic.com), separately from your Claude plan.
Options: (a) each user's own API key, (b) an office proxy server holding the key,
(c) defer — build with recorded/mock responses until later.
*Recommendation:* (a) for now, (b) before sharing with other designers.

**D3 — Office template and design standards**
Is there an existing office .dwt (layers, styles, labels)? Which rules apply: Israeli
accessibility regulations (route and ramp slopes, landings, handrails), municipal
landscape and tree requirements, water-wise planting guidelines?
*Recommendation:* send one real template and the key rule tables; the agent turns them
into rule files.

**D4 — Survey and background formats**
What do survey, trees and lot borders usually arrive as? (DWG with points and blocks,
PNEZD CSV, LandXML, SHP/GIS, PDF.) Is the tree survey a separate table with species and
sizes? Coordinate system: Israeli TM Grid (ITM)?
*Recommendation:* start with a DWG survey + tree table CSV in ITM.

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

## Decided
_(none yet)_
