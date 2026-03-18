namespace ArkaneSystems.Raven.Client.Console.Models;

// Holds the session ID of the conversation currently active in the REPL.
// Injected as a singleton so ConsoleLoop and RavenApiClient always share
// the same instance and see each other's updates.
public class SessionState
{
    public string SessionId { get; set; } = string.Empty;
}
