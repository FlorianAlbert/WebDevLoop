using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Events;

namespace WebDevLoop.Infrastructure.Events;

/// <summary>Stores a workflow event as (type name, JSON payload). Every concrete <see cref="WorkflowEvent"/> in Core is known by its type name.</summary>
internal static class WorkflowEventSerializer
{
    private static readonly IReadOnlyDictionary<string, Type> EventTypes = typeof(WorkflowEvent).Assembly
        .GetTypes()
        .Where(type => type.IsSubclassOf(typeof(WorkflowEvent)) && !type.IsAbstract)
        .ToDictionary(type => type.Name);

    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static (string Type, string PayloadJson) Serialize(WorkflowEvent workflowEvent)
    {
        Type type = workflowEvent.GetType();
        return (type.Name, JsonSerializer.Serialize(workflowEvent, type, Options));
    }

    public static WorkflowEvent Deserialize(string type, string payloadJson)
    {
        if (!EventTypes.TryGetValue(type, out Type? eventType))
        {
            throw new JsonException($"Unknown workflow event type '{type}'.");
        }

        return (WorkflowEvent?)JsonSerializer.Deserialize(payloadJson, eventType, Options)
            ?? throw new JsonException($"Workflow event payload of type '{type}' is null.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new StringIdConverter<RunId>(value => new RunId(value), id => id.Value));
        options.Converters.Add(new StringIdConverter<TicketRunId>(value => new TicketRunId(value), id => id.Value));
        options.Converters.Add(new StringIdConverter<StepRunId>(value => new StepRunId(value), id => id.Value));
        return options;
    }

    private sealed class StringIdConverter<TId>(Func<string, TId> create, Func<TId, string> value) : JsonConverter<TId>
    {
        public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            create(reader.GetString() ?? throw new JsonException($"{typeof(TId).Name} must be a string."));

        public override void Write(Utf8JsonWriter writer, TId id, JsonSerializerOptions options) => writer.WriteStringValue(value(id));
    }
}
