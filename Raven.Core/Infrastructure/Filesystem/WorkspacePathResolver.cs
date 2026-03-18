using Microsoft.Extensions.Configuration;

namespace ArkaneSystems.Raven.Core.Infrastructure.Filesystem;

public static class WorkspacePathResolver
{
  public static string ResolveWorkspaceRoot (IConfiguration configuration)
  {
    var configuredRoot = configuration["Raven:Workspace:RootPath"];
    if (!string.IsNullOrWhiteSpace (configuredRoot))
    {
      return Path.GetFullPath (configuredRoot);
    }

    var dedicatedEnvironmentRoot = Environment.GetEnvironmentVariable("RAVEN_WORKSPACE_ROOT");
    if (!string.IsNullOrWhiteSpace (dedicatedEnvironmentRoot))
    {
      return Path.GetFullPath (dedicatedEnvironmentRoot);
    }

    if (IsRunningInContainer ())
    {
      return "/data/workspace";
    }

    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return Path.Combine (localAppData, "Arkane Systems", "Raven", "Workspace");
  }

  private static bool IsRunningInContainer ()
  {
    var value = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
    return string.Equals (value, "true", StringComparison.OrdinalIgnoreCase)
           || string.Equals (value, "1", StringComparison.OrdinalIgnoreCase);
  }
}