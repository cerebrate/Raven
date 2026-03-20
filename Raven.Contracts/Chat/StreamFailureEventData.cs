namespace ArkaneSystems.Raven.Contracts.Chat;

// Structured payload sent on SSE failed events to enable machine-readable
// client handling while preserving an error message for display.
public record StreamFailureEventData(
    string Message,
    string? Code,
    bool IsRetryable);
