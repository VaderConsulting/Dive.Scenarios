using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using Dive.Scenarios.Models.Canonical;

namespace Dive.Scenarios.Adapters;

/// <summary>
/// Adapts schema v1.0 scenarios into the canonical runtime model.
/// </summary>
public sealed class ScenarioAdapterV1 : IScenarioAdapter
{
    /// <inheritdoc />
    public string SupportedSchemaVersion
    {
        get
        {
            return "1.0";
        }
    }

    /// <inheritdoc />
    public ScenarioRuntimeDefinition Adapt(Stream SourceStream, string SourceIdentifier)
    {
        using JsonDocument Document = JsonDocument.Parse(SourceStream);
        JsonElement Root = Document.RootElement;

        ScenarioRuntimeDefinition RuntimeDefinition = new ScenarioRuntimeDefinition
        {
            Metadata = new ScenarioMetadata
            {
                Name = ReadNestedString(Root, "metadata", "title") ?? Path.GetFileNameWithoutExtension(SourceIdentifier),
                Description = ReadNestedString(Root, "metadata", "description") ?? string.Empty,
                SourceSchemaVersion = SupportedSchemaVersion,
                SourceFile = SourceIdentifier,
                CompatibilityMode = ScenarioInteractionMode.ScriptedProcedures
            },
            Timeline = new TimelineSettings
            {
                DurationSeconds = 3600,
                TimeStepSeconds = 1,
                HumanActionSubstepSeconds = 0.25
            },
            Environment = new EnvironmentSettings
            {
                WaterType = ReadNestedString(Root, "environment", "waterType") ?? "salt",
                ExposureMedium = "water"
            },
            HumanActions = new HumanActionSettings
            {
                DefaultInteractionMode = ScenarioInteractionMode.ScriptedProcedures
            }
        };

        MapDivers(Root, RuntimeDefinition);
        MapTeams(Root, RuntimeDefinition);
        MapProcedureSchedule(Root, RuntimeDefinition);
        MapTimelineEvents(Root, RuntimeDefinition);
        RuntimeDefinition.MigrationDiagnostics.Add($"Info: Adapted schema {SupportedSchemaVersion} from '{SourceIdentifier}'.");

        return RuntimeDefinition;
    }

    private static void MapDivers(JsonElement Root, ScenarioRuntimeDefinition RuntimeDefinition)
    {
        if (!Root.TryGetProperty("divers", out JsonElement DiversElement)
            || DiversElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement DiverElement in DiversElement.EnumerateArray())
        {
            string DiverId = DiverElement.TryGetProperty("id", out JsonElement IdElement)
                ? IdElement.GetString() ?? TeamSimulationFrameBuilder.PRIMARY_DIVER_ID
                : TeamSimulationFrameBuilder.PRIMARY_DIVER_ID;
            DiverRuntimeModel Diver = new DiverRuntimeModel
            {
                DiverId = DiverId,
                Human = new HumanProfile
                {
                    WorkloadWatts = ReadNestedDouble(DiverElement, "initialState", "workload") ?? 100.0
                },
                Breathing = new BreathingProfile
                {
                    Mode = "open-circuit",
                    InitialBreathingInterfaceId = ReadNestedString(DiverElement, "initialState", "breathingGas")
                }
            };
            RuntimeDefinition.Divers.Add(Diver);
        }
    }

    private static void MapTeams(JsonElement Root, ScenarioRuntimeDefinition RuntimeDefinition)
    {
        if (!Root.TryGetProperty("teams", out JsonElement TeamsElement)
            || TeamsElement.ValueKind != JsonValueKind.Array
            || TeamsElement.GetArrayLength() == 0)
        {
            return;
        }

        JsonElement FirstTeam = TeamsElement[0];
        RuntimeDefinition.Team = new TeamRuntimeModel
        {
            TeamId = FirstTeam.TryGetProperty("id", out JsonElement IdElement)
                ? IdElement.GetString() ?? "team-primary"
                : "team-primary",
            Members = ReadStringArray(FirstTeam, "diverIds")
        };
    }

    private static void MapProcedureSchedule(JsonElement Root, ScenarioRuntimeDefinition RuntimeDefinition)
    {
        IReadOnlyList<ScenarioProcedureScheduleEntry> Entries = ScenarioProcedureScheduleReader.Read(Root);
        foreach (ScenarioProcedureScheduleEntry Entry in Entries)
        {
            Dictionary<string, string> ActorAssignments = Entry.ActorAssignments
                .ToDictionary(Assignment => Assignment.Role, Assignment => Assignment.ActorId);
            Dictionary<string, object> Parameters = new Dictionary<string, object>();
            foreach (ScenarioProcedureParameterValue Parameter in Entry.Parameters)
            {
                object Value = Parameter.EntityIdValue ?? Parameter.TextValue ?? string.Empty;
                Parameters[Parameter.ParameterKey] = Value;
            }

            RuntimeDefinition.ProcedureSchedule.Add(new ProcedureRequest
            {
                EventId = Entry.EventId,
                TimeSeconds = (int)Math.Round(Entry.TimeSeconds),
                Sequence = Entry.Sequence,
                ProcedureId = Entry.ProcedureId,
                ProcedureVersion = Entry.ProcedureVersion,
                ActorAssignments = ActorAssignments,
                Parameters = Parameters
            });
        }
    }

    private static void MapTimelineEvents(JsonElement Root, ScenarioRuntimeDefinition RuntimeDefinition)
    {
        if (!Root.TryGetProperty("timeline", out JsonElement TimelineElement)
            || TimelineElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        int Sequence = 0;
        foreach (JsonElement EventElement in TimelineElement.EnumerateArray())
        {
            Sequence++;
            string? EventType = EventElement.TryGetProperty("type", out JsonElement TypeElement)
                ? TypeElement.GetString()
                : null;
            if (string.Equals(EventType, "StartHumanProcedure", StringComparison.Ordinal))
            {
                continue;
            }

            double TimeSeconds = ResolveTimeSeconds(EventElement);
            string EventId = EventElement.TryGetProperty("eventId", out JsonElement EventIdElement)
                ? EventIdElement.GetString() ?? $"event-{Sequence}"
                : $"event-{Sequence}";

            if (string.Equals(EventType, "IncidentOccurrence", StringComparison.Ordinal))
            {
                RuntimeDefinition.IncidentSchedule.Add(new IncidentOccurrence
                {
                    EventId = EventId,
                    TimeSeconds = (int)Math.Round(TimeSeconds),
                    Sequence = Sequence,
                    IncidentCode = EventElement.TryGetProperty("incidentId", out JsonElement IncidentElement)
                        ? IncidentElement.GetString() ?? string.Empty
                        : string.Empty,
                    AffectedEquipmentInstanceId = EventElement.TryGetProperty("target", out JsonElement TargetElement)
                        ? TargetElement.GetString() ?? string.Empty
                        : string.Empty
                });
                continue;
            }

            if (string.Equals(EventType, "StateDeclaration", StringComparison.Ordinal))
            {
                RuntimeDefinition.StateDeclarations.Add(new StateDeclaration
                {
                    EventId = EventId,
                    TimeSeconds = (int)Math.Round(TimeSeconds),
                    Sequence = Sequence,
                    Target = EventElement.TryGetProperty("target", out JsonElement TargetElement)
                        ? TargetElement.GetString() ?? string.Empty
                        : string.Empty,
                    Value = EventElement.TryGetProperty("value", out JsonElement ValueElement)
                        ? ValueElement.ToString()
                        : string.Empty,
                    Semantics = "Explicit"
                });
            }
        }
    }

    private static double ResolveTimeSeconds(JsonElement EventElement)
    {
        if (EventElement.TryGetProperty("timeSeconds", out JsonElement TimeSecondsElement))
        {
            return TimeSecondsElement.GetDouble();
        }

        if (EventElement.TryGetProperty("timestamp", out JsonElement TimestampElement))
        {
            TimeSpan Timestamp = XmlConvert.ToTimeSpan(TimestampElement.GetString()!);
            return Timestamp.TotalSeconds;
        }

        return 0.0;
    }

    private static string? ReadNestedString(JsonElement Root, string ObjectName, string PropertyName)
    {
        if (!Root.TryGetProperty(ObjectName, out JsonElement ObjectElement))
        {
            return null;
        }

        if (!ObjectElement.TryGetProperty(PropertyName, out JsonElement PropertyElement))
        {
            return null;
        }

        return PropertyElement.GetString();
    }

    private static double? ReadNestedDouble(JsonElement Root, string ObjectName, string PropertyName)
    {
        if (!Root.TryGetProperty(ObjectName, out JsonElement ObjectElement))
        {
            return null;
        }

        if (!ObjectElement.TryGetProperty(PropertyName, out JsonElement PropertyElement))
        {
            return null;
        }

        if (PropertyElement.ValueKind == JsonValueKind.Number)
        {
            return PropertyElement.GetDouble();
        }

        return null;
    }

    private static List<string> ReadStringArray(JsonElement Root, string PropertyName)
    {
        List<string> Values = new List<string>();
        if (!Root.TryGetProperty(PropertyName, out JsonElement ArrayElement)
            || ArrayElement.ValueKind != JsonValueKind.Array)
        {
            return Values;
        }

        foreach (JsonElement ItemElement in ArrayElement.EnumerateArray())
        {
            string? Value = ItemElement.GetString();
            if (!string.IsNullOrWhiteSpace(Value))
            {
                Values.Add(Value);
            }
        }

        return Values;
    }
}
