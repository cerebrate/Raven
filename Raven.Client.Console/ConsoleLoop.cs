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
      var input = System.Console.ReadLine();

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
        { await client.DeleteSessionAsync (oldSessionId); }
        catch { /* best-effort */ }
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

      // Any other input is treated as a chat message. Stream the agent's
      // reply back and write each chunk as it arrives so the user sees
      // the response building up in real time.
      try
      {
        renderer.BeginResponse ();
        await foreach (var chunk in client.StreamMessageAsync (state.SessionId, input, cancellationToken))
          renderer.WriteChunk (chunk);
        renderer.EndResponse ();
      }
      catch (Exception ex)
      {
        renderer.ShowError (ex.Message);
      }
    }

    renderer.ShowGoodbye ();
  }
}