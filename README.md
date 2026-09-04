# Dive.Scenarios

**Source last updated:** 2026-07-06

C# adapter that maps Dive scenario schema v1.0 JSON into the canonical runtime model used by the Dive simulator. It reads metadata, environment, divers, teams, procedure schedules, and timeline events, and fills `ScenarioRuntimeDefinition` (default 3600 s timeline, salt water). This leftover folder contains the v1 adapter only, not the full Dive product.

**Language:** C#  
**Target:** .NET (class library fragment; no `.csproj` in this leftover folder)  
**Output:** source fragment (adapter)

## Solution structure

| Project | Language | Type | Purpose |
|---------|----------|------|---------|
| `ScenarioAdapterV1` | C# | adapter | Map Dive scenario schema v1.0 JSON into the canonical runtime model |

## How to open

There is no `.sln` / `.csproj` in this leftover tree. Add `Adapters/ScenarioAdapterV1.cs` to the Dive solution that already defines `Dive.Scenarios.Models.Canonical`.

## Requirements

- Visual Studio 2019 or later, or .NET SDK
- .NET 5.0 or later (System.Text.Json)

## Attribution and provenance

Dave Robinson / VaderConsulting. Historical Dev leftover related to the private Dive product. Namespace `Dive.Scenarios.Adapters`.

## License

MIT © 2026 VaderConsulting for Dave's code. See `LICENSE`.
