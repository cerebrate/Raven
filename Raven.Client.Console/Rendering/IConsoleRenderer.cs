using ArkaneSystems.Raven.Contracts.Chat;

namespace ArkaneSystems.Raven.Client.Console.Rendering;

// Abstracts all terminal output for the console client.
// Keeping presentation out of ConsoleLoop makes the loop easier to follow,
// and makes it possible to substitute a test double that captures output
// without touching the real terminal.
public interface IConsoleRenderer
{
    // Display the application banner on startup.
    void ShowBanner();

    // Confirm that a session has been established and show the session ID.
    void ShowSessionStarted(string sessionId);

    // Display the list of available slash-commands.
    void ShowHelp();

    // Write the prompt character(s) that precede user input (no newline).
    void WriteUserPrompt();

    // Write the agent label before streaming begins (no newline).
    void BeginResponse();

    // Write one streamed text chunk inline — called repeatedly as chunks arrive.
    void WriteChunk(string chunk);

    // Finalise the response (newline, spacing) after all chunks have been written.
    void EndResponse();

    // Display an error message.
    void ShowError(string message);

    // Display the farewell message when the user exits.
    void ShowGoodbye();

    // Display session metadata (used by the /history command).
    void ShowSessionInfo(SessionInfoResponse info);

    // Confirm that the old session was closed and a new one has started.
    void ShowNewSession(string oldSessionId, string newSessionId);
}
