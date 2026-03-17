namespace ArkaneSystems.Raven.Contracts.Chat;

public record SessionInfoResponse(
    string SessionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastActivityAt);
