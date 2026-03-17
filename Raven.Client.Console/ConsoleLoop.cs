using ArkaneSystems.Raven.Client.Console.Models;
using ArkaneSystems.Raven.Client.Console.Rendering;
using ArkaneSystems.Raven.Client.Console.Services;

namespace ArkaneSystems.Raven.Client.Console;

public class ConsoleLoop(RavenApiClient client, SessionState state, IConsoleRenderer renderer)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        renderer.ShowBanner();

        state.SessionId = await client.CreateSessionAsync();
        renderer.ShowSessionStarted(state.SessionId);

        while (!cancellationToken.IsCancellationRequested)
        {
            renderer.WriteUserPrompt();
            var input = System.Console.ReadLine();

            if (input is null || input.Equals("/exit", StringComparison.OrdinalIgnoreCase))
                break;

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("/help", StringComparison.OrdinalIgnoreCase))
            {
                renderer.ShowHelp();
                continue;
            }

            if (input.Equals("/new", StringComparison.OrdinalIgnoreCase))
            {
                var oldSessionId = state.SessionId;
                try { await client.DeleteSessionAsync(oldSessionId); } catch { /* best-effort */ }
                state.SessionId = await client.CreateSessionAsync();
                renderer.ShowNewSession(oldSessionId, state.SessionId);
                continue;
            }

            if (input.Equals("/history", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var info = await client.GetSessionAsync(state.SessionId);
                    if (info is not null)
                        renderer.ShowSessionInfo(info);
                    else
                        renderer.ShowError("Session not found.");
                }
                catch (Exception ex)
                {
                    renderer.ShowError(ex.Message);
                }
                continue;
            }

            try
            {
                renderer.BeginResponse();
                await foreach (var chunk in client.StreamMessageAsync(state.SessionId, input, cancellationToken))
                    renderer.WriteChunk(chunk);
                renderer.EndResponse();
            }
            catch (Exception ex)
            {
                renderer.ShowError(ex.Message);
            }
        }

        renderer.ShowGoodbye();
    }
}
