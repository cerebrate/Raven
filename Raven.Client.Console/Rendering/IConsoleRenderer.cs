using ArkaneSystems.Raven.Contracts.Chat;

namespace ArkaneSystems.Raven.Client.Console.Rendering;

public interface IConsoleRenderer
{
    void ShowBanner();
    void ShowSessionStarted(string sessionId);
    void ShowHelp();
    void WriteUserPrompt();
    void BeginResponse();
    void WriteChunk(string chunk);
    void EndResponse();
    void ShowError(string message);
    void ShowGoodbye();
    void ShowSessionInfo(SessionInfoResponse info);
    void ShowNewSession(string oldSessionId, string newSessionId);
}
