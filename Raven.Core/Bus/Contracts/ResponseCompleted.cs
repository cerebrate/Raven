namespace ArkaneSystems.Raven.Core.Bus.Contracts;

// Terminal success event for a streaming response sequence.
public sealed record ResponseCompleted(
    string ResponseId,
    DateTimeOffset CompletedAtUtc,
    string? FinalContent = null);
