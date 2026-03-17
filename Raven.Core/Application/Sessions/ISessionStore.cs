namespace ArkaneSystems.Raven.Core.Application.Sessions;

public interface ISessionStore
{
    Task<string> CreateSessionAsync();
    Task<bool> SessionExistsAsync(string sessionId);
}
