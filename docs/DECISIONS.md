# Decisions

## Open (for the next meeting)

**D1 — Civil 3D version and test setup**
Which Civil 3D release(s) must be supported (e.g. 2025, 2026, 2027)? This sets the .NET
target (.NET 8 for 2025+) and API surface. Who tests in Civil 3D, and how often?
*Recommendation:* target your current release only; you test each "Please test" PR
when convenient and report in the meeting.

**D2 — How the plugin talks to Claude**
The plugin calls the Claude API from inside Civil 3D. API usage is billed to an
Anthropic API account (console.anthropic.com), separately from your Claude plan.
Options: (a) your own API key per user, (b) a small company proxy server holding the key,
(c) defer — build with recorded/mock responses until later.
*Recommendation:* (a) for now, (b) before sharing with other designers.

**D3 — Templates and standards**
Is there an existing company .dwt with styles, label sets and parts lists, and which
design standard applies (e.g. Israeli road/drainage standards, municipal requirements)?
*Recommendation:* send one real template and the standard's key tables; the agent
builds the rule files from them.

**D4 — Survey and lot data formats**
What do survey and lot borders usually arrive as? (PNEZD points CSV, LandXML, DWG
linework, SHP/GIS, PDF.) Which coordinate system (Israeli TM Grid / ITM)?
*Recommendation:* start with points CSV + DWG lot lines in ITM.

**D5 — First target project type**
Pick the first kind of design to make work end-to-end: (a) residential subdivision
(lots + local roads + storm), (b) single plot site grading + access, (c) road/interchange.
*Recommendation:* (b) — smallest complete loop, then grow to (a).

**D6 — Utilities scope**
Which networks are in scope for v1: storm, sewer, water, none?
*Recommendation:* storm only in v1.

## Decided
_(none yet)_
