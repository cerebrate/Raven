using ArkaneSystems.Raven.Core.Infrastructure.Filesystem;
using Microsoft.Extensions.Configuration;

namespace ArkaneSystems.Raven.Core.Tests.Unit.Infrastructure;

public sealed class WorkspacePathResolverTests
{
  [Fact]
  public void ResolveWorkspaceRoot_ReturnsConfiguredRoot_WhenConfigurationValueIsProvided ()
  {
    var configuredRoot = Path.Combine(Path.GetTempPath(), "Raven.Core.Tests", Guid.NewGuid().ToString("N"));

    var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
              ["Raven:Workspace:RootPath"] = configuredRoot
            })
            .Build();

    var resolvedRoot = WorkspacePathResolver.ResolveWorkspaceRoot(configuration);

    Assert.Equal (Path.GetFullPath (configuredRoot), resolvedRoot);
  }
}