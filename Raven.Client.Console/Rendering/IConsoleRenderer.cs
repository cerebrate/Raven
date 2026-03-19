using ArkaneSystems.Raven.Contracts.Chat;

namespace ArkaneSystems.Raven.Client.Console.Rendering;

// Abstracts all terminal output for the console client.
// Keeping presentation out of ConsoleLoop makes the loop easier to follow,
// and makes it possible to substitute a test double that captures output
// without touching the real terminal.
public interface IConsoleRenderer
{
  // Display the application banner on startup.
  void ShowBanner ();

  // Confirm that a session has been established and show the session ID.
  void ShowSessionStarted (string sessionId);

  // Display the list of available slash-commands.
  void ShowHelp ();

  // Write the prompt character(s) that precede user input (no newline).
  void WriteUserPrompt ();

  // Render a streaming agent response as Markdown inside a Live display region.
  // Accumulates chunks as they arrive, re-rendering the Markdown on each update,
  // and stops the Live region once the stream is exhausted.
  Task RenderResponseStreamAsync (IAsyncEnumerable<string> chunks, CancellationToken cancellationToken = default);

  // Display an error message.
  void ShowError (string message);

  // Display a warning message.
  void ShowWarning (string message);

  // Prompt shown when stale-session recovery requires creating a new session.
  void ShowStaleSessionRecoveryPrompt ();

  // Display the farewell message when the user exits.
  void ShowGoodbye ();

  // Display session metadata (used by the /history command).
  void ShowSessionInfo (SessionInfoResponse info);

  // Confirm that the old session was closed and a new one has started.
  void ShowNewSession (string oldSessionId, string newSessionId);
}