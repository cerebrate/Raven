namespace ArkaneSystems.Raven.Client.Console.Services;

// Thrown when the server sends a server_shutdown SSE event during an active
// response stream, indicating that the server is about to shut down or restart.
public sealed class ServerShuttingDownException (bool isRestart)
    : Exception (isRestart
        ? "The server is restarting. Please reconnect after it comes back online."
        : "The server is shutting down.")
{
  public bool IsRestart { get; } = isRestart;
}
