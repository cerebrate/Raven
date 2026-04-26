using ArkaneSystems.Raven.Core.Bus.Contracts;
using ArkaneSystems.Raven.Core.Bus.Dispatch;
using Microsoft.Extensions.Hosting;

namespace ArkaneSystems.Raven.Core.Application.Admin;

// Process exit codes used by Raven.Core.
// The Docker entrypoint script or container restart policy uses these to
// distinguish a deliberate restart from a clean shutdown.
public static class ExitCodes
{
  // Graceful shutdown — the container runner should NOT restart the process.
  public const int Shutdown = 0;

  // Deliberate restart requested by an admin — the container runner SHOULD restart.
  // Using 42 (rather than 1) makes the intent explicit and avoids confusion with
  // generic non-zero exit codes produced by unhandled exceptions.
  public const int Restart = 42;
}

// Singleton service that coordinates a graceful server shutdown or restart.
// The first call to RequestShutdownAsync:
//   1. Sets IsShutdownRequested so new streaming requests are rejected.
//   2. Broadcasts a ServerShuttingDown event to every active SSE response stream
//      so connected clients can display a warning before the connection drops.
//   3. Sets Environment.ExitCode to communicate the intended action to the runner.
//   4. Schedules StopApplication() after a 1-second grace period so the HTTP
//      response for the admin command can be flushed before the socket closes.
// Subsequent calls are no-ops (idempotent).
public sealed class ShutdownCoordinator (
    IResponseStreamEventHub streamHub,
    IHostApplicationLifetime lifetime,
    ILogger<ShutdownCoordinator> logger) : IShutdownCoordinator
{
  // 0 = no shutdown in progress; 1 = shutdown/restart initiated.
  // Uses Interlocked to guarantee only one caller proceeds.
  private int _shutdownInitiated;

  public bool IsShutdownRequested => Volatile.Read (ref _shutdownInitiated) == 1;

  public async Task RequestShutdownAsync (bool restart, CancellationToken cancellationToken = default)
  {
    // Idempotency guard — only the first caller proceeds; all others return immediately.
    if (Interlocked.CompareExchange (ref _shutdownInitiated, 1, 0) != 0)
    {
      logger.LogInformation ("Shutdown/restart already in progress; ignoring duplicate request.");
      return;
    }

    var action = restart ? "restart" : "shutdown";
    logger.LogInformation ("Admin {Action} requested. Notifying active response streams.", action);

    // Broadcast a shutdown notification to every session that currently has an
    // active SSE response stream. Best-effort: a failure on one stream must not
    // prevent the others from being notified.
    var activeStreamIds = streamHub.GetActiveStreamIds ();
    foreach (var responseId in activeStreamIds)
    {
      try
      {
        var envelope = new ResponseStreamEventEnvelope (
            MessageMetadata.Create ("server.shutdown.v1"),
            new ServerShuttingDown (responseId, restart));

        await streamHub.PublishAsync (envelope, cancellationToken);
        streamHub.Complete (responseId);
      }
      catch (Exception ex)
      {
        logger.LogWarning (ex, "Failed to notify response stream {ResponseId} of {Action}.", responseId, action);
      }
    }

    logger.LogInformation (
        "Notified {Count} active stream(s). Scheduling host stop with exit code {ExitCode}.",
        activeStreamIds.Count,
        restart ? ExitCodes.Restart : ExitCodes.Shutdown);

    // Set the process exit code before stopping the host so the OS / container
    // runner sees the intended value.
    Environment.ExitCode = restart ? ExitCodes.Restart : ExitCodes.Shutdown;

    // Fire-and-forget: wait a short grace period so the HTTP response for the
    // admin request can be fully written and flushed before Kestrel stops.
    _ = Task.Run (async () =>
    {
      await Task.Delay (TimeSpan.FromSeconds (1), CancellationToken.None);

      try
      {
        lifetime.StopApplication ();
      }
      catch (Exception ex)
      {
        logger.LogError (ex, "Failed to stop the application during {Action}. The process may need to be terminated manually.", action);
      }
    }, CancellationToken.None);
  }
}
