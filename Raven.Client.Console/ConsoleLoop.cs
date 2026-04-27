#region header

// Raven.Client.Console - ConsoleLoop.cs
// 
// Alistair J. R. Young
// Arkane Systems
// 
// Copyright Arkane Systems 2012-2026.  All rights reserved.
// 
// Created: 2026-04-27 4:02 PM

#endregion

#region using

using ArkaneSystems.Raven.Client.Console.Models;
using ArkaneSystems.Raven.Client.Console.Rendering;
using ArkaneSystems.Raven.Client.Console.Services;
using ArkaneSystems.Raven.Contracts.Chat;
using JetBrains.Annotations;

#endregion

namespace ArkaneSystems.Raven.Client.Console;

// The main REPL (Read-Eval-Print Loop) for the console client.
// Runs until the user types /exit or closes the terminal.
// All terminal output is delegated to IConsoleRenderer so this class stays
// focused on flow control rather than presentation.
public class ConsoleLoop (RavenApiClient client, SessionState state, IConsoleRenderer renderer)
{
  // resumeSessionId is set when the user passes --resume <id> on the command line.
  // When present the REPL skips creating a new session and attaches to the
  // existing one instead.
  //
  // selectMode is set when the user passes --select on the command line.
  // When true and no resumeSessionId is provided, the REPL shows a numbered
  // session-selection menu and lets the user pick before entering the loop.
  public async Task RunAsync (string?           resumeSessionId   = null,
                              bool              selectMode        = false,
                              CancellationToken cancellationToken = default)
  {
    renderer.ShowBanner ();

    bool    isResumed    = false;
    string? sessionTitle = null;

    if (!string.IsNullOrWhiteSpace (resumeSessionId))
    {
      // Validate the session exists before attaching to it.
      SessionInfoResponse? info = await client.GetSessionAsync (resumeSessionId);

      if (info is null)
      {
        renderer.ShowError ($"Session '{resumeSessionId}' not found. Starting a new session instead.");
        state.SessionId = await client.CreateSessionAsync ();
      }
      else
      {
        state.SessionId = resumeSessionId;
        sessionTitle    = info.Title;
        isResumed       = true;
      }
    }
    else if (selectMode)
    {
      // --select: fetch existing sessions and let the user pick one or start new.
      IReadOnlyList<SessionSummary> sessions = await client.ListSessionsAsync ();
      renderer.ShowSessionSelectionMenu (sessions);

      if (sessions.Count > 0)
      {
        string?         raw           = await ReadLineWithCancellationAsync (cancellationToken);
        SessionSummary? chosenSession = ResolveSelectionChoice (raw: raw, sessions: sessions);

        if (chosenSession is not null)
        {
          state.SessionId = chosenSession.SessionId;
          sessionTitle    = chosenSession.Title;
          isResumed       = true;
        }
        else
        {
          // 0, empty, or out-of-range → new session
          state.SessionId = await client.CreateSessionAsync ();
        }
      }
      else
      {
        // No sessions available; create a new one immediately.
        state.SessionId = await client.CreateSessionAsync ();
      }
    }
    else
    {
      // Create a session with Raven.Core before entering the loop.
      // This registers a new conversation with the agent and persists
      // the session record to SQLite.
      state.SessionId = await client.CreateSessionAsync ();
    }

    // Show the session title (if known) centered between the Figlet and the
    // "AI Assistant" separator Rule, then the session-started line.
    renderer.ShowSessionHeader (sessionTitle);
    renderer.ShowSessionStarted (sessionId: state.SessionId, isResumed: isResumed, title: sessionTitle);

    // loopCts is cancelled either by the outer token (process shutdown) or by
    // the notification listener when the server announces a shutdown/restart.
    // Using a linked source means the REPL exits cleanly on either signal
    // without needing to poll or check flags in every branch.
    using CancellationTokenSource loopCts = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);

    // Track a server-initiated shutdown/restart so we can display the right
    // message after the loop exits. Null means no server notification arrived.
    bool? serverShutdownIsRestart = null;

    // Tracks whether a server shutdown was already surfaced via
    // ServerShuttingDownException (mid-stream case) so we don't show
    // ShowAdminCommandAccepted twice.
    bool serverShutdownHandled = false;

    // Background task: subscribes to the server notification channel and
    // cancels loopCts when a shutdown/restart notification arrives. This
    // ensures idle clients (not currently streaming) are notified promptly.
    Task notificationTask = this.MonitorNotificationsAsync (sessionId: state.SessionId,
                                                            onServerShutdown: isRestart =>
                                                                              {
                                                                                serverShutdownIsRestart = isRestart;
                                                                                loopCts.Cancel ();
                                                                              },
                                                            cancellationToken: loopCts.Token);

    try
    {
      while (!loopCts.Token.IsCancellationRequested)
      {
        renderer.WriteUserPrompt ();
        string? input = await ReadLineWithCancellationAsync (loopCts.Token);

        // null means the input stream was closed (e.g. Ctrl+Z / EOF).
        if (input is null || input.Equals (value: "/exit", comparisonType: StringComparison.OrdinalIgnoreCase))
        {
          break;
        }

        if (string.IsNullOrWhiteSpace (input))
        {
          continue;
        }

        if (input.Equals (value: "/help", comparisonType: StringComparison.OrdinalIgnoreCase))
        {
          renderer.ShowHelp ();

          continue;
        }

        // /new: delete the current session server-side (best-effort — ignore
        // errors in case it was already removed) then start a fresh one.
        if (input.Equals (value: "/new", comparisonType: StringComparison.OrdinalIgnoreCase))
        {
          string oldSessionId = state.SessionId;

          try
          {
            await client.DeleteSessionAsync (oldSessionId);
          }
          catch
          {
            // best-effort
          }

          state.SessionId = await client.CreateSessionAsync ();
          sessionTitle    = null;  // new session starts without a title
          isResumed       = false; // session was created, not resumed
          renderer.ShowNewSession (oldSessionId: oldSessionId, newSessionId: state.SessionId);

          continue;
        }

        // /sessions: list all resumable sessions on the server.
        if (input.Equals (value: "/sessions", comparisonType: StringComparison.OrdinalIgnoreCase))
        {
          try
          {
            IReadOnlyList<SessionSummary> sessions = await client.ListSessionsAsync ();
            renderer.ShowSessionList (sessions);
          }
          catch (Exception ex)
          {
            renderer.ShowError (ex.Message);
          }

          continue;
        }

        // /resume <sessionId>: switch the current REPL to an existing session.
        // The notification monitor continues on the current channel since
        // shutdown events are broadcast to all active sessions regardless.
        if (input.StartsWith (value: "/resume ", comparisonType: StringComparison.OrdinalIgnoreCase))
        {
          string targetId = input["/resume ".Length..].Trim ();

          if (string.IsNullOrWhiteSpace (targetId))
          {
            renderer.ShowWarning ("Usage: /resume <session-id>");

            continue;
          }

          try
          {
            SessionInfoResponse? info = await client.GetSessionAsync (targetId);

            if (info is null)
            {
              renderer.ShowError ($"Session '{targetId}' not found.");
            }
            else
            {
              state.SessionId = targetId;
              sessionTitle    = info.Title;
              renderer.ShowSessionStarted (sessionId: state.SessionId, isResumed: true, title: info.Title);
            }
          }
          catch (Exception ex)
          {
            renderer.ShowError (ex.Message);
          }

          continue;
        }

        // /history: fetch and display metadata for the current session.
        if (input.Equals (value: "/history", comparisonType: StringComparison.OrdinalIgnoreCase))
        {
          try
          {
            SessionInfoResponse? info = await client.GetSessionAsync (state.SessionId);

            if (info is not null)
            {
              renderer.ShowSessionInfo (info);
            }
            else
            {
              renderer.ShowError ("Session not found.");
            }
          }
          catch (Exception ex)
          {
            renderer.ShowError (ex.Message);
          }

          continue;
        }

        // /admin:shutdown and /admin:restart: admin commands that stop or restart
        // Raven.Core. The /admin: prefix visually distinguishes these destructive
        // operations from ordinary session commands (/new, /history) and makes it
        // obvious in the /help table that they affect all connected clients.
        // Both require explicit confirmation ("yes") before the request is sent.
        if (input.Equals (value: "/admin:shutdown", comparisonType: StringComparison.OrdinalIgnoreCase) ||
            input.Equals (value: "/admin:restart",  comparisonType: StringComparison.OrdinalIgnoreCase))
        {
          bool isRestart = input.Equals (value: "/admin:restart", comparisonType: StringComparison.OrdinalIgnoreCase);

          renderer.ShowAdminCommandConfirmationPrompt (isRestart);
          string? confirmation = await ReadLineWithCancellationAsync (loopCts.Token);

          if (loopCts.Token.IsCancellationRequested)
          {
            break;
          }

          if (!string.Equals (a: confirmation, b: "yes", comparisonType: StringComparison.OrdinalIgnoreCase))
          {
            renderer.ShowWarning ("Cancelled.");

            continue;
          }

          try
          {
            if (isRestart)
            {
              await client.RequestRestartAsync ();
            }
            else
            {
              await client.RequestShutdownAsync ();
            }

            renderer.ShowAdminCommandAccepted (isRestart);
          }
          catch (Exception ex)
          {
            renderer.ShowError (ex.Message);

            continue;
          }

          // The server is stopping; exit the client loop cleanly.
          break;
        }

        // Any other input is treated as a chat message. The renderer owns the full
        // response lifecycle: it streams chunks, accumulates them while showing
        // status/progress during streaming, then renders the full Markdown response once.
        try
        {
          await renderer.RenderResponseStreamAsync (chunks: client.StreamMessageAsync (sessionId: state.SessionId,
                                                                                       content: input,
                                                                                       cancellationToken: loopCts.Token),
                                                    cancellationToken: loopCts.Token);

          // After each successful exchange, check whether the server has generated
          // (or updated) the session title.  This typically fires after the first
          // exchange for new sessions.  The check is best-effort: failures are
          // silently swallowed so they never interrupt the conversation flow.
          if (sessionTitle is null)
          {
            try
            {
              SessionInfoResponse? refreshed = await client.GetSessionAsync (state.SessionId);

              if (!string.IsNullOrWhiteSpace (refreshed?.Title))
              {
                sessionTitle = refreshed.Title;
                renderer.ShowTitleSet (sessionTitle);
              }
            }
            catch
            {
              // best-effort — ignore any error; title will be shown next time
            }
          }
        }
        catch (StreamEventFailedException ex) when (string.Equals (a: ex.Code,
                                                                   b: "session_stale",
                                                                   comparisonType: StringComparison.Ordinal))
        {
          renderer.ShowWarning ("The current session is stale and can no longer be used.");
          renderer.ShowStaleSessionRecoveryPrompt ();

          _ = await ReadLineWithCancellationAsync (loopCts.Token);

          if (loopCts.Token.IsCancellationRequested)
          {
            break;
          }

          string oldSessionId = state.SessionId;
          state.SessionId = await client.CreateSessionAsync ();
          renderer.ShowNewSession (oldSessionId: oldSessionId, newSessionId: state.SessionId);
        }
        catch (ServerShuttingDownException ex)
        {
          // The server is shutting down or restarting mid-stream. Display the
          // appropriate message and exit the client loop. The notification task
          // may also fire around the same time; the post-loop check below is
          // guarded so the message is shown exactly once.
          renderer.ShowAdminCommandAccepted (ex.IsRestart);
          serverShutdownHandled = true;

          break;
        }
        catch (Exception ex)
        {
          renderer.ShowError (ex.Message);
        }
      }
    }
    finally
    {
      // Cancel the notification task and wait for it to finish so it does not
      // linger after RunAsync returns.
      loopCts.Cancel ();

      try
      {
        await notificationTask;
      }
      catch (OperationCanceledException)
      {
        // Expected when loopCts is cancelled.
      }
    }

    // If the loop was interrupted by a server notification (and not already
    // handled by ServerShuttingDownException above), show the appropriate message.
    if (serverShutdownIsRestart.HasValue && !serverShutdownHandled)
    {
      renderer.ShowAdminCommandAccepted (serverShutdownIsRestart.Value);
    }

    renderer.ShowGoodbye (sessionId: state.SessionId, title: sessionTitle);
  }

  // Subscribes to the server notification channel and invokes onServerShutdown
  // when a server_shutdown event is received. Connection errors are treated as
  // graceful end-of-stream so the background task never crashes the REPL.
  private async Task MonitorNotificationsAsync (string            sessionId,
                                                Action<bool>      onServerShutdown,
                                                CancellationToken cancellationToken)
  {
    try
    {
      await foreach (ServerNotification notification in client.SubscribeToNotificationsAsync (sessionId: sessionId,
                                                                                              cancellationToken: cancellationToken))
      {
        if (string.Equals (a: notification.EventType, b: "server_shutdown", comparisonType: StringComparison.OrdinalIgnoreCase))
        {
          bool isRestart = string.Equals (a: notification.Data, b: "restart", comparisonType: StringComparison.OrdinalIgnoreCase);
          onServerShutdown (isRestart);

          return; // one shutdown per session is enough
        }

        // Unknown event types are silently ignored — forward compatibility.
      }
    }
    catch (OperationCanceledException)
    {
      // Normal exit when loopCts is cancelled.
    }
    catch (Exception)
    {
      // Connection errors (server restart, network issues) are silently swallowed
      // so the background task never surfaces an unhandled exception. The user
      // will discover the disconnection when they next send a message.
    }
  }

  // Reads a line from the console, returning null immediately if the
  // cancellationToken fires while waiting so the REPL can exit cleanly.
  // Note: the underlying Task.Run thread remains blocked on Console.ReadLine
  // until the user actually presses Enter; it is reclaimed when the process exits.
  private static async Task<string?> ReadLineWithCancellationAsync (CancellationToken cancellationToken)
  {
    if (cancellationToken.IsCancellationRequested)
    {
      return null;
    }

    Task<string?> readTask = Task.Run (function: System.Console.ReadLine, cancellationToken: CancellationToken.None);
    await Task.WhenAny (task1: readTask,
                        task2: Task.Delay (millisecondsDelay: Timeout.Infinite, cancellationToken: cancellationToken));

    return cancellationToken.IsCancellationRequested ? null : await readTask;
  }

  // Parses the raw input from a session-selection menu and returns the chosen
  // session summary, or null if the user chose to start a new session (input
  // is empty, "0", or an out-of-range number).
  private static SessionSummary? ResolveSelectionChoice (string? raw, IReadOnlyList<SessionSummary> sessions)
  {
    if (string.IsNullOrWhiteSpace (raw))
    {
      return null;
    }

    if (!int.TryParse (s: raw.Trim (), result: out int choice))
    {
      return null;
    }

    if ((choice < 1) || (choice > sessions.Count))
    {
      return null;
    }

    return sessions[choice - 1];
  }
}