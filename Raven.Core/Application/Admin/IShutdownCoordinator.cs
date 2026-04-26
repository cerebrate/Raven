namespace ArkaneSystems.Raven.Core.Application.Admin;

// Coordinates a graceful server shutdown or restart by notifying all active
// response streams and then stopping the host after a short grace period.
public interface IShutdownCoordinator
{
  // True once a shutdown or restart has been requested.
  bool IsShutdownRequested { get; }

  // Initiates a graceful shutdown or restart. If restart is true, the process
  // exits with ExitCodes.Restart so that the entrypoint script can relaunch
  // it in-place without exiting the pod. Calling this method more than once
  // is a no-op.
  Task RequestShutdownAsync (bool restart, CancellationToken cancellationToken = default);
}
