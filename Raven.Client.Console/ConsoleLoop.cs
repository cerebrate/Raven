using static global::System.Console;
using ArkaneSystems.Raven.Client.Console.Models;
using ArkaneSystems.Raven.Client.Console.Services;

namespace ArkaneSystems.Raven.Client.Console;

public class ConsoleLoop(RavenApiClient client, SessionState state)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        WriteLine("Connecting to Raven...");
        state.SessionId = await client.CreateSessionAsync();
        WriteLine($"Session started. Type /exit to quit.");
        WriteLine();

        while (!cancellationToken.IsCancellationRequested)
        {
            Write("> ");
            var input = ReadLine();

            if (input is null || input.Equals("/exit", StringComparison.OrdinalIgnoreCase))
                break;

            if (string.IsNullOrWhiteSpace(input))
                continue;

            var response = await client.SendMessageAsync(state.SessionId, input);
            WriteLine($"Raven: {response}");
            WriteLine();
        }

        WriteLine("Goodbye.");
    }
}
