using ArkaneSystems.Raven.Client.Console.Models;
using ArkaneSystems.Raven.Client.Console.Rendering;
using ArkaneSystems.Raven.Client.Console.Services;

namespace ArkaneSystems.Raven.Client.Console;

// The main REPL (Read-Eval-Print Loop) for the console client.
// Runs until the user types /exit or closes the terminal.
// All terminal output is delegated to IConsoleRenderer so this class stays
// focused on flow control rather than presentation.
public class ConsoleLoop (RavenApiClient client, SessionState state, IConsoleRenderer renderer)
{
  public async Task RunAsync (CancellationToken cancellationToken = default)
  {
    renderer.ShowBanner ();

    // Create a session with Raven.Core before entering the loop.
    // This registers a new conversation with the agent and persists
    // the session record to SQLite.
    state.SessionId = await client.CreateSessionAsync ();
    renderer.ShowSessionStarted (state.SessionId);

    while (!cancellationToken.IsCancellationRequested)
    {
      renderer.WriteUserPrompt ();
      var input = await ReadLineWithCancellationAsync (cancellationToken);

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
        renderer.ShowNewSession (oldSessionId, state.SessionId);
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

      // Any other input is treated as a chat message. The renderer owns the full
      // response lifecycle: it streams chunks, accumulates them while showing
      // status/progress during streaming, then renders the full Markdown response once.
      try
      {
        await renderer.RenderResponseStreamAsync (
            client.StreamMessageAsync (state.SessionId, input, cancellationToken),
            cancellationToken);
      }
      catch (StreamEventFailedException ex) when (string.Equals (ex.Code, "session_stale", StringComparison.Ordinal))
      {
        renderer.ShowWarning ("The current session is stale and can no longer be used.");
        renderer.ShowStaleSessionRecoveryPrompt ();

        _ = await ReadLineWithCancellationAsync (cancellationToken);
        if (cancellationToken.IsCancellationRequested)
          break;

        var oldSessionId = state.SessionId;
        state.SessionId = await client.CreateSessionAsync ();
        renderer.ShowNewSession (oldSessionId, state.SessionId);
      }
      catch (Exception ex)
      {
        renderer.ShowError (ex.Message);
      }
    }

    renderer.ShowGoodbye ();
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
}