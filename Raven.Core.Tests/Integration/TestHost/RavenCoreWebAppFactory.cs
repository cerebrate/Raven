using ArkaneSystems.Raven.Core.AgentRuntime;
using ArkaneSystems.Raven.Core.Application.Admin;
using ArkaneSystems.Raven.Core.Application.Sessions;
using ArkaneSystems.Raven.Core.Tests.Integration.TestHost.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArkaneSystems.Raven.Core.Tests.Integration.TestHost;

public sealed class RavenCoreWebAppFactory : WebApplicationFactory<Program>
{
  private readonly string _workspaceRoot = Path.Combine(
        Path.GetTempPath(),
        "Raven.Core.Tests",
        Guid.NewGuid().ToString("N"));
  private readonly string? _previousWorkspaceRoot;

  public string WorkspaceRoot => this._workspaceRoot;

  public RavenCoreWebAppFactory ()
  {
    this._previousWorkspaceRoot = Environment.GetEnvironmentVariable ("RAVEN_WORKSPACE_ROOT");
    Environment.SetEnvironmentVariable ("RAVEN_WORKSPACE_ROOT", this._workspaceRoot);
  }

  protected override void ConfigureWebHost (IWebHostBuilder builder)
  {
    builder.UseEnvironment ("Development");

    builder.ConfigureAppConfiguration ((_, configurationBuilder) => configurationBuilder.AddInMemoryCollection (new Dictionary<string, string?>
    {
      ["Raven:Workspace:RootPath"] = this._workspaceRoot
    }));

    builder.ConfigureServices (services =>
    {
      services.RemoveAll<IAgentConversationService> ();
      services.RemoveAll<ISessionStore> ();
      services.RemoveAll<ISessionSnapshotStore> ();
      services.RemoveAll<IShutdownCoordinator> ();

      services.AddSingleton<IAgentConversationService, FakeAgentConversationService> ();
      services.AddSingleton<ISessionStore, InMemorySessionStore> ();
      services.AddSingleton<ISessionSnapshotStore, InMemorySessionSnapshotStore> ();
      services.AddSingleton<FakeShutdownCoordinator> ();
      services.AddSingleton<IShutdownCoordinator> (sp => sp.GetRequiredService<FakeShutdownCoordinator> ());
    });
  }

  protected override void Dispose (bool disposing)
  {
    try
    {
      if (Directory.Exists (this._workspaceRoot))
      {
        Directory.Delete (this._workspaceRoot, recursive: true);
      }
    }
    catch (IOException)
    {
      // Best-effort cleanup; ignore IO-related errors during test teardown.
    }
    catch (UnauthorizedAccessException)
    {
      // Best-effort cleanup; ignore permission issues during test teardown.
    }

    Environment.SetEnvironmentVariable ("RAVEN_WORKSPACE_ROOT", this._previousWorkspaceRoot);
    base.Dispose (disposing);
  }
}