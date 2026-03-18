using Microsoft.Extensions.Logging;

namespace ArkaneSystems.Raven.Core.Bus.Dispatch;

// Default dead-letter sink that emits structured warning logs.
public sealed class LoggingDeadLetterSink (ILogger<LoggingDeadLetterSink> logger) : IDeadLetterSink
{
  public Task WriteAsync (DeadLetterEntry entry, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(entry);

    logger.LogWarning(
        "Dead-lettered message {MessageId} type {MessageType} payload {PayloadType}. Reason: {Reason}. ExceptionType: {ExceptionType}",
        entry.Metadata.MessageId,
        entry.Metadata.Type,
        entry.PayloadType,
        entry.Reason,
        entry.ExceptionType);

    return Task.CompletedTask;
  }
}
