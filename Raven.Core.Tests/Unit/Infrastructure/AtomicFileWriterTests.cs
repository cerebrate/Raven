using ArkaneSystems.Raven.Core.Infrastructure.Filesystem;
using System.Text;

namespace ArkaneSystems.Raven.Core.Tests.Unit.Infrastructure;

public sealed class AtomicFileWriterTests
{
  [Fact]
  public async Task WriteAllTextAsync_CreatesTargetFile_WithExpectedContent ()
  {
    var workspaceRoot = Path.Combine(Path.GetTempPath(), "Raven.Core.Tests", Guid.NewGuid().ToString("N"));
    var targetPath = Path.Combine(workspaceRoot, "config", "appsettings.json");

    try
    {
      await AtomicFileWriter.WriteAllTextAsync(targetPath, "{\"name\":\"raven\"}", TestContext.Current.CancellationToken);

      var content = await File.ReadAllTextAsync(targetPath, TestContext.Current.CancellationToken);
      Assert.Equal("{\"name\":\"raven\"}", content);
    }
    finally
    {
      if (Directory.Exists(workspaceRoot))
      {
        Directory.Delete(workspaceRoot, recursive: true);
      }
    }
  }

  [Fact]
  public async Task WriteAllBytesAsync_ReplacesExistingFile_WithNewContent ()
  {
    var workspaceRoot = Path.Combine(Path.GetTempPath(), "Raven.Core.Tests", Guid.NewGuid().ToString("N"));
    var targetPath = Path.Combine(workspaceRoot, "audit", "entry.log");

    try
    {
      Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
      await File.WriteAllTextAsync(targetPath, "old", TestContext.Current.CancellationToken);

      await AtomicFileWriter.WriteAllBytesAsync(targetPath, Encoding.UTF8.GetBytes("new"), TestContext.Current.CancellationToken);

      var content = await File.ReadAllTextAsync(targetPath, TestContext.Current.CancellationToken);
      Assert.Equal("new", content);
    }
    finally
    {
      if (Directory.Exists(workspaceRoot))
      {
        Directory.Delete(workspaceRoot, recursive: true);
      }
    }
  }
}
