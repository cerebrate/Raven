namespace ArkaneSystems.Raven.Core.Application.Sessions;

public interface ISessionStore
{
    Task<string> CreateSessionAsync(string conversationId);
    Task<bool> SessionExistsAsync(string sessionId);
    Task<string?> GetConversationIdAsync(string sessionId);
    Task<SessionInfo?> GetSessionAsync(string sessionId);
    Task<bool> DeleteSessionAsync(string sessionId);
}

public record SessionInfo(string SessionId, DateTimeOffset CreatedAt, DateTimeOffset? LastActivityAt);
