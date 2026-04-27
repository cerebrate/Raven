#region header

// Raven.Client.Console - IConsoleRenderer.cs
// 
// Alistair J. R. Young
// Arkane Systems
// 
// Copyright Arkane Systems 2012-2026.  All rights reserved.
// 
// Created: 2026-04-27 12:05 PM

#endregion

#region using

using ArkaneSystems.Raven.Contracts.Chat;
using JetBrains.Annotations;

#endregion

namespace ArkaneSystems.Raven.Client.Console.Rendering;

// Abstracts all terminal output for the console client.
// Keeping presentation out of ConsoleLoop makes the loop easier to follow,
// and makes it possible to substitute a test double that captures output
// without touching the real terminal.
public interface IConsoleRenderer
{
  // Display the application banner on startup (Figlet only; the separator Rule
  // is deferred until the session title is known — see ShowSessionHeader).
  void ShowBanner ();

  // Display the session-title area: an optional centered title (if non-null/empty)
  // followed by the horizontal "AI Assistant" separator Rule.
  // Called once after session creation/resumption, immediately before ShowSessionStarted.
  void ShowSessionHeader (string? title);

  // Confirm that a session has been established and show the session ID.
  // When resuming an existing session, showResumed is true so the display
  // message reflects "resumed" rather than "started".
  void ShowSessionStarted (string sessionId, bool isResumed = false);

  // Overload that also displays the session title for resumed sessions so
  // the user knows which conversation they rejoined without inspecting the ID.
  void ShowSessionStarted (string sessionId, bool isResumed, string? title);

  // Display the list of available slash-commands.
  void ShowHelp ();

  // Write the prompt character(s) that precede user input (no newline).
  void WriteUserPrompt ();

  // Render a streaming agent response to the console.
  // The default SpectreConsoleRenderer implementation runs an AnsiConsole.Status spinner
  // while accumulating chunks, then renders the full accumulated Markdown once when the stream completes.
  Task RenderResponseStreamAsync (IAsyncEnumerable<string> chunks, CancellationToken cancellationToken = default);

  // Display an error message.
  void ShowError (string message);

  // Display a warning message.
  void ShowWarning (string message);

  // Notify the user that a session title was generated or updated.
  // Called inline in the chat flow after the first exchange (or any title regeneration).
  void ShowTitleSet (string title);

  // Prompt shown when stale-session recovery requires creating a new session.
  void ShowStaleSessionRecoveryPrompt ();

  // Display the farewell message when the user exits, showing the session ID so
  // the user can resume the same session in a future invocation.
  void ShowGoodbye (string? sessionId = null);

  // Overload that also shows the session title when available.
  void ShowGoodbye (string? sessionId, string? title);

  // Display session metadata (used by the /history command).
  void ShowSessionInfo (SessionInfoResponse info);

  // Confirm that the old session was closed and a new one has started.
  void ShowNewSession (string oldSessionId, string newSessionId);

  // Display a list of resumable sessions for the /sessions command.
  void ShowSessionList (IReadOnlyList<SessionSummary> sessions);

  // Display a numbered session-selection menu at startup (for --select mode).
  // Prints the list and a prompt asking the user to enter a number or press
  // Enter to start a new session.  ConsoleLoop reads the raw input.
  void ShowSessionSelectionMenu (IReadOnlyList<SessionSummary> sessions);

  // Prompt shown when the user enters /admin:shutdown or /admin:restart to obtain
  // confirmation before the command is sent to the server.
  void ShowAdminCommandConfirmationPrompt (bool isRestart);

  // Displayed after the server accepts a /admin:shutdown or /admin:restart command,
  // and also when the notification channel delivers a server_shutdown event to an
  // idle client (not currently streaming a chat response).
  void ShowAdminCommandAccepted (bool isRestart);
}