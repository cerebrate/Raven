namespace ArkaneSystems.Raven.Contracts.Chat;

// A summary of a single resumable session returned by GET /api/chat/sessions.
// Carries enough metadata for a client to display a selection menu without
// a separate request per session.
public record SessionSummary (
    string          SessionId,
    DateTimeOffset  CreatedAt,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset  SnapshotAt,
    string?         Title = null);
