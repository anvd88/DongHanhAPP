using System.Text.Json;

namespace KetoanMini.Api.BuildingBlocks.Messaging;

/// <summary>
/// Immutable, versioned integration-event envelope. Events are invalidation hints; business data is
/// always fetched again from an authorized REST endpoint.
/// </summary>
public sealed record IntegrationEventEnvelope(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    string Producer,
    string? AggregateId,
    long? AggregateVersion,
    string? Actor,
    string? CorrelationId,
    string? CausationId,
    string[] Audience,
    JsonElement Data);

public static class IntegrationEventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
