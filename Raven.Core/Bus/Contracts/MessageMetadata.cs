namespace ArkaneSystems.Raven.Core.Bus.Contracts;

// Envelope metadata that must flow across Raven runtime boundaries for
// traceability, causality, and policy decisions.
public sealed record MessageMetadata(
    string MessageId,
    string CorrelationId,
    string? CausationId,
    string? SessionId,
    string? UserId,
    string Type,
    MessagePriority Priority,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AvailableAtUtc = null)
{
  // Helper for creating metadata with deterministic defaults while allowing
  // callers to override correlation/session identity as needed.
  public static MessageMetadata Create(
      string type,
      string? correlationId = null,
      string? causationId = null,
      string? sessionId = null,
      string? userId = null,
      MessagePriority priority = MessagePriority.Normal,
      DateTimeOffset? createdAtUtc = null,
      DateTimeOffset? availableAtUtc = null)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(type);

    return new MessageMetadata(
        MessageId: Guid.NewGuid().ToString(),
        CorrelationId: string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString() : correlationId,
        CausationId: causationId,
        SessionId: sessionId,
        UserId: userId,
        Type: type,
        Priority: priority,
        CreatedAtUtc: createdAtUtc ?? DateTimeOffset.UtcNow,
        AvailableAtUtc: availableAtUtc);
  }
}
