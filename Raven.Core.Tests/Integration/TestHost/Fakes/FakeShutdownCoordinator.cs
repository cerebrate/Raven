using ArkaneSystems.Raven.Core.Application.Admin;

namespace ArkaneSystems.Raven.Core.Tests.Integration.TestHost.Fakes;

// Test double for IShutdownCoordinator. Records calls without actually stopping
// the application, so integration tests can exercise the admin endpoints safely.
public sealed class FakeShutdownCoordinator : IShutdownCoordinator
{
  public bool IsShutdownRequested { get; private set; }

  // The restart flag passed to the most recent RequestShutdownAsync call.
  public bool? LastRequestedRestart { get; private set; }

  public Task RequestShutdownAsync (bool restart, CancellationToken cancellationToken = default)
  {
    IsShutdownRequested = true;
    LastRequestedRestart = restart;
    return Task.CompletedTask;
  }

  public void Reset ()
  {
    IsShutdownRequested = false;
    LastRequestedRestart = null;
  }
}
