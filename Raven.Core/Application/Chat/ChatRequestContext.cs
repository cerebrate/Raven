namespace ArkaneSystems.Raven.Core.Application.Chat;

// Context propagated from transport boundary into application chat flows.
public sealed record ChatRequestContext(
    string? CorrelationId,
    string? UserId)
{
  public static ChatRequestContext Empty { get; } = new(null, null);
}
