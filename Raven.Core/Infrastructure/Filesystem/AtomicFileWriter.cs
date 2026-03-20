using System.Text;

namespace ArkaneSystems.Raven.Core.Infrastructure.Filesystem;

// Provides crash-safer file persistence for workspace-owned critical files.
public static class AtomicFileWriter
{
  /// <summary>
  /// Writes text content to a file using a temporary file and atomic replace semantics.
  /// </summary>
  public static Task WriteAllTextAsync (
      string destinationPath,
      string content,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(content);
    return WriteAllBytesAsync(destinationPath, Encoding.UTF8.GetBytes(content), cancellationToken);
  }

  /// <summary>
  /// Writes byte content to a file using a temporary file and atomic replace semantics.
  /// </summary>
  public static async Task WriteAllBytesAsync (
      string destinationPath,
      byte[] content,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
    ArgumentNullException.ThrowIfNull(content);

    cancellationToken.ThrowIfCancellationRequested();

    var destinationDirectory = Path.GetDirectoryName(destinationPath);
    if (string.IsNullOrWhiteSpace(destinationDirectory))
    {
      throw new ArgumentException("Destination path must include a directory.", nameof(destinationPath));
    }

    Directory.CreateDirectory(destinationDirectory);

    var destinationFullPath = Path.GetFullPath(destinationPath);
    var tempFileName = $".{Path.GetFileName(destinationFullPath)}.{Guid.NewGuid():N}.tmp";
    var tempFilePath = Path.Combine(destinationDirectory, tempFileName);

    try
    {
      await using (var tempStream = new FileStream(
                       tempFilePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough | FileOptions.Asynchronous))
      {
        await tempStream.WriteAsync(content, cancellationToken);
        await tempStream.FlushAsync(cancellationToken);
      }

      if (File.Exists(destinationFullPath))
      {
        File.Replace(tempFilePath, destinationFullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
      }
      else
      {
        File.Move(tempFilePath, destinationFullPath);
      }
    }
    finally
    {
      if (File.Exists(tempFilePath))
      {
        File.Delete(tempFilePath);
      }
    }
  }
}
