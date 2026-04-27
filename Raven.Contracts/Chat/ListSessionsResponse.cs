namespace ArkaneSystems.Raven.Contracts.Chat;

// Response body returned from GET /api/chat/sessions.
// Lists all sessions that currently have a valid snapshot and can therefore
// be resumed by the client without losing conversation context.
public record ListSessionsResponse (IReadOnlyList<SessionSummary> Sessions);
