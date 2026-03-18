using ArkaneSystems.Raven.Core.AgentRuntime;
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

  public RavenCoreWebAppFactory ()
  {
    _previousWorkspaceRoot = Environment.GetEnvironmentVariable ("RAVEN_WORKSPACE_ROOT");
    Environment.SetEnvironmentVariable ("RAVEN_WORKSPACE_ROOT", _workspaceRoot);
  }

  protected override void ConfigureWebHost (IWebHostBuilder builder)
  {
    builder.UseEnvironment ("Development");

    builder.ConfigureAppConfiguration ((_, configurationBuilder) =>
    {
      configurationBuilder.AddInMemoryCollection (new Dictionary<string, string?>
      {
        ["Raven:Workspace:RootPath"] = _workspaceRoot
      });
    });

    builder.ConfigureServices (services =>
    {
      services.RemoveAll<IAgentConversationService> ();
      services.RemoveAll<ISessionStore> ();

      services.AddSingleton<IAgentConversationService, FakeAgentConversationService> ();
      services.AddSingleton<ISessionStore, InMemorySessionStore> ();
    });
  }

  protected override void Dispose (bool disposing)
  {
    Environment.SetEnvironmentVariable ("RAVEN_WORKSPACE_ROOT", _previousWorkspaceRoot);
    base.Dispose (disposing);
  }
}