using System.Text.Json;
using Claims.Domain.Common;

namespace Claims.Infrastructure.Persistence;

/// <summary>
/// Row in the transactional outbox. Written in the same SaveChanges as the aggregate change, then
/// relayed to Kafka by <see cref="Messaging.OutboxPublisherService"/> (at-least-once delivery).
/// </summary>
public class OutboxMessage
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Guid Id { get; set; }
    public string Type { get; set; } = default!;
    /// <summary>Kafka message key: the claim id, so all events of one claim stay ordered in one partition.</summary>
    public string Key { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    public static OutboxMessage From(DomainEvent ev)
    {
        var id = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            eventId = id,
            eventType = ev.EventType,
            claimId = ev.ClaimId,
            claimNumber = ev.ClaimNumber,
            occurredAt = ev.OccurredAt,
            data = ev.Data
        }, Json);

        return new OutboxMessage
        {
            Id = id,
            Type = ev.EventType,
            Key = ev.ClaimId.ToString(),
            Payload = payload,
            OccurredAt = ev.OccurredAt
        };
    }
}
