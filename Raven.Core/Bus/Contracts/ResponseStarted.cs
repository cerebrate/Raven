namespace ArkaneSystems.Raven.Core.Bus.Contracts;

// First event emitted for a streaming response sequence.
public sealed record ResponseStarted(
    string ResponseId,
    DateTimeOffset StartedAtUtc,
    string? ContentType = "text/plain");
