using System.Text;
using System.Text.Json;
using Claims.Application.Services;
using Claims.Infrastructure.Persistence;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Claims.Infrastructure.Messaging;

public interface IEventPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct);
}

/// <summary>Used when Kafka is disabled (Kafka:Enabled=false). Honest no-broker mode: events are only logged.</summary>
public sealed class LoggingEventPublisher : IEventPublisher
{
    private readonly ILogger<LoggingEventPublisher> _log;
    public LoggingEventPublisher(ILogger<LoggingEventPublisher> log) => _log = log;

    public Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        _log.LogInformation("[Kafka disabled] would publish {EventType} for claim {Key}: {Payload}", message.Type, message.Key, message.Payload);
        return Task.CompletedTask;
    }
}

public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;

    public KafkaEventPublisher(IOptions<KafkaOptions> options)
    {
        _topic = options.Value.Topic;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10_000
        }).Build();
    }

    public async Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        var kafkaMessage = new Message<string, string>
        {
            Key = message.Key,
            Value = message.Payload,
            Headers = new Headers
            {
                { "event-type", Encoding.UTF8.GetBytes(message.Type) },
                { "event-id", Encoding.UTF8.GetBytes(message.Id.ToString()) }
            }
        };
        await _producer.ProduceAsync(_topic, kafkaMessage, ct);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}

/// <summary>
/// Outbox relay. Polls unprocessed rows in order and publishes them. At-least-once: a crash between
/// publish and commit re-sends the message, so consumers must be idempotent. A failing message stops
/// the batch (preserving order) and is parked after <see cref="MaxAttempts"/> attempts.
/// </summary>
public sealed class OutboxPublisherService : BackgroundService
{
    public const int MaxAttempts = 10;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopes;
    private readonly IEventPublisher _publisher;
    private readonly TimeProvider _clock;
    private readonly ILogger<OutboxPublisherService> _log;

    public OutboxPublisherService(IServiceScopeFactory scopes, IEventPublisher publisher, TimeProvider clock, ILogger<OutboxPublisherService> log)
    {
        _scopes = scopes;
        _publisher = publisher;
        _clock = clock;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Outbox publisher started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessBatchAsync(stoppingToken);
                if (processed == 0) await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Outbox batch failed; retrying");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();

        var batch = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.Attempts < MaxAttempts)
            .OrderBy(m => m.OccurredAt).ThenBy(m => m.Id)
            .Take(50).ToListAsync(ct);
        if (batch.Count == 0) return 0;

        var published = 0;
        foreach (var message in batch)
        {
            try
            {
                await _publisher.PublishAsync(message, ct);
                message.ProcessedAt = _clock.GetUtcNow().UtcDateTime;
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                _log.LogWarning(ex, "Publishing outbox message {MessageId} ({Type}) failed, attempt {Attempt}", message.Id, message.Type, message.Attempts);
                break; // keep ordering: do not publish later messages ahead of a failed one
            }
        }

        await db.SaveChangesAsync(CancellationToken.None);
        if (published > 0) _log.LogInformation("Outbox: published {Count} event(s)", published);
        return published;
    }
}

/// <summary>
/// Demonstrates asynchronous claim processing. Consumes the claims topic and reacts to ClaimSubmitted by
/// auto-assigning the claim. Other events are logged as where a notification service would hook in.
/// Failed messages are retried 3 times and then logged and skipped: there is no dead-letter topic (see TRADE-OFFS.md).
/// </summary>
public sealed class ClaimEventConsumerService : BackgroundService
{
    private readonly KafkaOptions _options;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ClaimEventConsumerService> _log;

    public ClaimEventConsumerService(IOptions<KafkaOptions> options, IServiceScopeFactory scopes, ILogger<ClaimEventConsumerService> log)
    {
        _options = options.Value;
        _scopes = scopes;
        _log = log;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => RunAsync(stoppingToken), stoppingToken);

    private async Task RunAsync(CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            AllowAutoCreateTopics = true
        };

        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => _log.LogWarning("Kafka consumer error: {Reason}", e.Reason))
            .Build();
        consumer.Subscribe(_options.Topic);
        _log.LogInformation("Kafka consumer subscribed to {Topic} as group {Group}", _options.Topic, _options.ConsumerGroup);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(ct);
                    if (result?.Message is null) continue;

                    await HandleWithRetryAsync(result.Message.Value, ct);
                    consumer.Commit(result);
                }
                catch (ConsumeException ex)
                {
                    _log.LogWarning(ex, "Kafka consume failed; backing off");
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task HandleWithRetryAsync(string payload, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await HandleAsync(payload, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "Handling event failed (attempt {Attempt}/3)", attempt);
                if (attempt < 3) await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
                else _log.LogError("Giving up on event after 3 attempts: {Payload}", payload);
            }
        }
    }

    private async Task HandleAsync(string payload, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var type = root.GetProperty("eventType").GetString();
        var claimId = root.GetProperty("claimId").GetGuid();
        var number = root.GetProperty("claimNumber").GetString();

        switch (type)
        {
            case "ClaimSubmitted":
                using (var scope = _scopes.CreateScope())
                {
                    var service = scope.ServiceProvider.GetRequiredService<AutoAssignmentService>();
                    await service.TryAutoAssignAsync(claimId, ct);
                }
                break;
            default:
                // Hook point for notification, analytics or fraud-scoring consumers.
                _log.LogInformation("Consumed {EventType} for claim {ClaimNumber}", type, number);
                break;
        }
    }
}
