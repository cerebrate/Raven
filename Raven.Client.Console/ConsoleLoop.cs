using ArkaneSystems.Raven.Client.Console.Models;
using ArkaneSystems.Raven.Client.Console.Rendering;
using ArkaneSystems.Raven.Client.Console.Services;
using ArkaneSystems.Raven.Contracts.Chat;

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
  public async Task RunAsync (string? resumeSessionId = null, bool selectMode = false, CancellationToken cancellationToken = default)
  {
    renderer.ShowBanner ();

    bool isResumed = false;
    string? sessionTitle = null;

    if (!string.IsNullOrWhiteSpace (resumeSessionId))
    {
      // Validate the session exists before attaching to it.
      var info = await client.GetSessionAsync (resumeSessionId);
      if (info is null)
      {
        renderer.ShowError ($"Session '{resumeSessionId}' not found. Starting a new session instead.");
        state.SessionId = await client.CreateSessionAsync ();
      }
      else
      {
        state.SessionId = resumeSessionId;
        sessionTitle = info.Title;
        isResumed = true;
      }
    }
    else if (selectMode)
    {
      // --select: fetch existing sessions and let the user pick one or start new.
      var sessions = await client.ListSessionsAsync ();
      renderer.ShowSessionSelectionMenu (sessions);

      if (sessions.Count > 0)
      {
        var raw = await ReadLineWithCancellationAsync (cancellationToken);
        var chosenSession = ResolveSelectionChoice (raw, sessions);
        if (chosenSession is not null)
        {
          state.SessionId = chosenSession.SessionId;
          sessionTitle = chosenSession.Title;
          isResumed = true;
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

    renderer.ShowSessionStarted (state.SessionId, isResumed, sessionTitle);

    // loopCts is cancelled either by the outer token (process shutdown) or by
    // the notification listener when the server announces a shutdown/restart.
    // Using a linked source means the REPL exits cleanly on either signal
    // without needing to poll or check flags in every branch.
    using var loopCts = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);

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
    var notificationTask = MonitorNotificationsAsync (
        state.SessionId,
        isRestart =>
        {
          serverShutdownIsRestart = isRestart;
          loopCts.Cancel ();
        },
        loopCts.Token);

    try
    {
      while (!loopCts.Token.IsCancellationRequested)
      {
        renderer.WriteUserPrompt ();
        var input = await ReadLineWithCancellationAsync (loopCts.Token);

        // null means the input stream was closed (e.g. Ctrl+Z / EOF).
        if (input is null || input.Equals ("/exit", StringComparison.OrdinalIgnoreCase))
          break;

        if (string.IsNullOrWhiteSpace (input))
          continue;

        if (input.Equals ("/help", StringComparison.OrdinalIgnoreCase))
        {
          renderer.ShowHelp ();
          continue;
        }

        // /new: delete the current session server-side (best-effort — ignore
        // errors in case it was already removed) then start a fresh one.
        if (input.Equals ("/new", StringComparison.OrdinalIgnoreCase))
        {
          var oldSessionId = state.SessionId;
          try
          {
            await client.DeleteSessionAsync (oldSessionId);
          }
          catch
          {
            // best-effort
          }

          state.SessionId = await client.CreateSessionAsync ();
          sessionTitle = null;   // new session starts without a title
          isResumed    = false;  // session was created, not resumed
          renderer.ShowNewSession (oldSessionId, state.SessionId);
          continue;
        }

        // /sessions: list all resumable sessions on the server.
        if (input.Equals ("/sessions", StringComparison.OrdinalIgnoreCase))
        {
          try
          {
            var sessions = await client.ListSessionsAsync ();
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
        if (input.StartsWith ("/resume ", StringComparison.OrdinalIgnoreCase))
        {
          var targetId = input["/resume ".Length..].Trim ();
          if (string.IsNullOrWhiteSpace (targetId))
          {
            renderer.ShowWarning ("Usage: /resume <session-id>");
            continue;
          }

          try
          {
            var info = await client.GetSessionAsync (targetId);
            if (info is null)
            {
              renderer.ShowError ($"Session '{targetId}' not found.");
            }
            else
            {
              state.SessionId = targetId;
              sessionTitle = info.Title;
              renderer.ShowSessionStarted (state.SessionId, isResumed: true, info.Title);
            }
          }
          catch (Exception ex)
          {
            renderer.ShowError (ex.Message);
          }

          continue;
        }

        // /history: fetch and display metadata for the current session.
        if (input.Equals ("/history", StringComparison.OrdinalIgnoreCase))
        {
          try
          {
            var info = await client.GetSessionAsync(state.SessionId);
            if (info is not null)
              renderer.ShowSessionInfo (info);
            else
              renderer.ShowError ("Session not found.");
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
        if (input.Equals ("/admin:shutdown", StringComparison.OrdinalIgnoreCase) ||
            input.Equals ("/admin:restart", StringComparison.OrdinalIgnoreCase))
        {
          var isRestart = input.Equals ("/admin:restart", StringComparison.OrdinalIgnoreCase);

          renderer.ShowAdminCommandConfirmationPrompt (isRestart);
          var confirmation = await ReadLineWithCancellationAsync (loopCts.Token);

          if (loopCts.Token.IsCancellationRequested)
            break;

          if (!string.Equals (confirmation, "yes", StringComparison.OrdinalIgnoreCase))
          {
            renderer.ShowWarning ("Cancelled.");
            continue;
          }

          try
          {
            if (isRestart)
              await client.RequestRestartAsync ();
            else
              await client.RequestShutdownAsync ();

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
          await renderer.RenderResponseStreamAsync (
              client.StreamMessageAsync (state.SessionId, input, loopCts.Token),
              loopCts.Token);
        }
        catch (StreamEventFailedException ex) when (string.Equals (ex.Code, "session_stale", StringComparison.Ordinal))
        {
          renderer.ShowWarning ("The current session is stale and can no longer be used.");
          renderer.ShowStaleSessionRecoveryPrompt ();

          _ = await ReadLineWithCancellationAsync (loopCts.Token);
          if (loopCts.Token.IsCancellationRequested)
            break;

          var oldSessionId = state.SessionId;
          state.SessionId = await client.CreateSessionAsync ();
          renderer.ShowNewSession (oldSessionId, state.SessionId);
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
      renderer.ShowAdminCommandAccepted (serverShutdownIsRestart.Value);

    renderer.ShowGoodbye (state.SessionId, sessionTitle);
  }

  // Subscribes to the server notification channel and invokes onServerShutdown
  // when a server_shutdown event is received. Connection errors are treated as
  // graceful end-of-stream so the background task never crashes the REPL.
  private async Task MonitorNotificationsAsync (
      string sessionId,
      Action<bool> onServerShutdown,
      CancellationToken cancellationToken)
  {
    try
    {
      await foreach (var notification in client.SubscribeToNotificationsAsync (sessionId, cancellationToken))
      {
        if (string.Equals (notification.EventType, "server_shutdown", StringComparison.OrdinalIgnoreCase))
        {
          var isRestart = string.Equals (notification.Data, "restart", StringComparison.OrdinalIgnoreCase);
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
      return null;

    var readTask = Task.Run (System.Console.ReadLine, CancellationToken.None);
    await Task.WhenAny (readTask, Task.Delay (Timeout.Infinite, cancellationToken));

    return cancellationToken.IsCancellationRequested ? null : await readTask;
  }

  // Parses the raw input from a session-selection menu and returns the chosen
  // session summary, or null if the user chose to start a new session (input
  // is empty, "0", or an out-of-range number).
  private static SessionSummary? ResolveSelectionChoice (string? raw, IReadOnlyList<SessionSummary> sessions)
  {
    if (string.IsNullOrWhiteSpace (raw))
      return null;

    if (!int.TryParse (raw.Trim (), out var choice))
      return null;

    if (choice < 1 || choice > sessions.Count)
      return null;

    return sessions[choice - 1];
  }
}