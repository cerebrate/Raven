namespace ArkaneSystems.Raven.Core.Application.Sessions;

public interface ISessionStore
{
    Task<string> CreateSessionAsync(string conversationId);
    Task<bool> SessionExistsAsync(string sessionId);
    Task<string?> GetConversationIdAsync(string sessionId);
}
