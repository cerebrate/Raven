using ArkaneSystems.Raven.Core.Bus.Contracts;
using ArkaneSystems.Raven.Core.Bus.Dispatch;
using Microsoft.Extensions.Hosting;

namespace ArkaneSystems.Raven.Core.Application.Admin;

// Process exit codes used by Raven.Core.
// The entrypoint script interprets these to decide what to do after the
// dotnet process exits.
public static class ExitCodes
{
  // Graceful shutdown — the entrypoint sleeps indefinitely so that
  // Kubernetes does NOT restart the pod.
  public const int Shutdown = 0;

  // Deliberate restart requested by an admin — the entrypoint relaunches
  // the dotnet process in-place without exiting the pod, providing a fast
  // configuration/skill reload. Using 42 (rather than 1) makes the intent
  // explicit and avoids confusion with generic error exit codes.
  public const int Restart = 42;
}

// Singleton service that coordinates a graceful server shutdown or restart.
// The first call to RequestShutdownAsync:
//   1. Sets IsShutdownRequested so new streaming requests are rejected.
//   2. Broadcasts a ServerShuttingDown event to every active SSE response stream
//      so mid-conversation clients can display a warning before the connection drops.
//   3. Broadcasts a ServerShutdownNotification to every session notification channel
//      so idle clients (not currently streaming) are also notified.
//   4. Sets Environment.ExitCode to communicate the intended action to the runner.
//   5. Schedules StopApplication() after a 1-second grace period so the HTTP
//      response for the admin command can be flushed before the socket closes.
// Subsequent calls are no-ops (idempotent).
public sealed class ShutdownCoordinator (
    IResponseStreamEventHub streamHub,
    ISessionNotificationHub notificationHub,
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

    // Broadcast a shutdown notification to every session that has an active
    // notification channel subscription, covering idle clients that are not
    // currently streaming a chat response.
    try
    {
      var notificationEnvelope = new ServerNotificationEnvelope (
          MessageMetadata.Create ("server.shutdown.v1"),
          new ServerShutdownNotification (restart));

      await notificationHub.BroadcastAsync (notificationEnvelope, cancellationToken);
    }
    catch (Exception ex)
    {
      logger.LogWarning (ex, "Failed to broadcast {Action} notification to session notification channels.", action);
    }

    logger.LogInformation (
        "Notified {StreamCount} active response stream(s) and {NotificationCount} notification subscriber(s). Scheduling host stop with exit code {ExitCode}.",
        activeStreamIds.Count,
        notificationHub.GetSubscribedSessionIds ().Count,
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
