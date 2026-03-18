namespace ArkaneSystems.Raven.Core.Bus.Contracts;

// Terminal failure event for a streaming response sequence.
public sealed record ResponseFailed(
    string ResponseId,
    string ErrorMessage,
    DateTimeOffset FailedAtUtc,
    string? ErrorCode = null,
    bool IsRetryable = false);
