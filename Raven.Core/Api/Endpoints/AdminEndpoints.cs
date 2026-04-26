using ArkaneSystems.Raven.Contracts.Admin;
using ArkaneSystems.Raven.Core.Application.Admin;

namespace ArkaneSystems.Raven.Core.Api.Endpoints;

// Extension class that registers admin HTTP endpoints onto the minimal API
// route builder. Called once from Program.cs via MapAdminEndpoints().
// Note: these endpoints are unauthenticated in the current implementation.
// Authentication and authorisation should be added before exposing the API
// to untrusted networks.
public static class AdminEndpoints
{
  public static IEndpointRouteBuilder MapAdminEndpoints (this IEndpointRouteBuilder app)
  {
    var group = app.MapGroup ("/api/admin");

    // POST /api/admin/shutdown
    // Initiates a graceful shutdown. All active SSE sessions are notified
    // before the host stops. Returns 202 Accepted immediately; the actual
    // shutdown happens after a short grace period so this response can be
    // flushed to the caller first.
    _ = group.MapPost ("/shutdown", async (
        IShutdownCoordinator shutdown,
        CancellationToken cancellationToken) =>
    {
      await shutdown.RequestShutdownAsync (restart: false, cancellationToken);
      return Results.Accepted (
          (string?) null,
          new AdminCommandResponse ("Shutdown initiated. The server will stop shortly."));
    });

    // POST /api/admin/restart
    // Initiates a graceful restart. All active SSE sessions are notified
    // before the host stops. Returns 202 Accepted immediately. The process
    // exits with ExitCodes.Restart (42) so the container orchestrator or
    // wrapper script can restart it.
    _ = group.MapPost ("/restart", async (
        IShutdownCoordinator shutdown,
        CancellationToken cancellationToken) =>
    {
      await shutdown.RequestShutdownAsync (restart: true, cancellationToken);
      return Results.Accepted (
          (string?) null,
          new AdminCommandResponse ("Restart initiated. The server will stop shortly and restart."));
    });

    return app;
  }
}
