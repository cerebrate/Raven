namespace ArkaneSystems.Raven.Core.Bus.Contracts;

// Incremental content fragment emitted after ResponseStarted.
public sealed record ResponseDelta(
    string ResponseId,
    int Sequence,
    string Content,
    DateTimeOffset EmittedAtUtc) : IResponseStreamEvent;
