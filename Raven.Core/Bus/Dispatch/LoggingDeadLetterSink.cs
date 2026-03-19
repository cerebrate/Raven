using Microsoft.Extensions.Logging;

namespace ArkaneSystems.Raven.Core.Bus.Dispatch;

// Default dead-letter sink that emits structured warning logs.
public sealed class LoggingDeadLetterSink (ILogger<LoggingDeadLetterSink> logger) : IDeadLetterSink
{
  public Task WriteAsync (DeadLetterEntry entry, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(entry);

    using var _ = logger.BeginScope(new Dictionary<string, object?>
    {
      ["MessageId"] = entry.Metadata.MessageId,
      ["CorrelationId"] = entry.Metadata.CorrelationId,
      ["CausationId"] = entry.Metadata.CausationId,
      ["SessionId"] = entry.Metadata.SessionId,
      ["UserId"] = entry.Metadata.UserId,
      ["MessageType"] = entry.Metadata.Type,
      ["PayloadType"] = entry.PayloadType,
      ["FailedAtUtc"] = entry.FailedAtUtc
    });

    logger.LogWarning(
        "Dead-lettered message type {MessageType}. Reason: {Reason}. ExceptionType: {ExceptionType}. ExceptionMessage: {ExceptionMessage}",
        entry.Metadata.Type,
        entry.Reason,
        entry.ExceptionType,
        entry.ExceptionMessage);

    return Task.CompletedTask;
  }
}
