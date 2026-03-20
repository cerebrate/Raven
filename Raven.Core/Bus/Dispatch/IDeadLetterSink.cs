using ArkaneSystems.Raven.Core.Bus.Contracts;

namespace ArkaneSystems.Raven.Core.Bus.Dispatch;

// Captures unrecoverable dispatch failures for diagnostics and replay.
public interface IDeadLetterSink
{
  Task WriteAsync (DeadLetterEntry entry, CancellationToken cancellationToken = default);
}

public sealed record DeadLetterEntry(
    MessageMetadata Metadata,
    string PayloadType,
    string Reason,
    DateTimeOffset FailedAtUtc,
    string? ExceptionType = null,
    string? ExceptionMessage = null);
