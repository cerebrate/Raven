using ArkaneSystems.Raven.Core.Application.Sessions;
using ArkaneSystems.Raven.Core.Infrastructure.Filesystem;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ArkaneSystems.Raven.Core.Infrastructure.Persistence;

public sealed class FileSessionEventLog (IWorkspacePaths workspacePaths) : ISessionEventLog
{
  private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

  private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new(StringComparer.Ordinal);

  /// <summary>
  /// Appends a single event as a JSON line to the per-session NDJSON log file.
  /// </summary>
  public async Task<SessionEventEnvelope> AppendAsync (
      string sessionId,
      string eventType,
      object payload,
      string? correlationId = null,
      string? userId = null,
      CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
    ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
    ArgumentNullException.ThrowIfNull(payload);

    cancellationToken.ThrowIfCancellationRequested();

    var gate = _sessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
    await gate.WaitAsync(cancellationToken);

    try
    {
      var filePath = GetLogFilePath(sessionId);
      var sequence = await GetNextSequenceAsync(filePath, cancellationToken);

      var envelope = new SessionEventEnvelope(
          EventId: Guid.NewGuid().ToString(),
          SessionId: sessionId,
          Sequence: sequence,
          EventType: eventType,
          OccurredAtUtc: DateTimeOffset.UtcNow,
          CorrelationId: correlationId,
          UserId: userId,
          SchemaVersion: 1,
          Payload: payload);

      var line = JsonSerializer.Serialize(envelope, SerializerOptions);

      await using var stream = new FileStream(
          filePath,
          FileMode.Append,
          FileAccess.Write,
          FileShare.Read,
          bufferSize: 4096,
          FileOptions.Asynchronous | FileOptions.WriteThrough);

      await using var writer = new StreamWriter(stream);
      await writer.WriteLineAsync(line);
      await writer.FlushAsync(cancellationToken);

      return envelope;
    }
    finally
    {
      _ = gate.Release();
    }
  }

  /// <summary>
  /// Reads all events in append order from the per-session NDJSON log file.
  /// </summary>
  public async IAsyncEnumerable<SessionEventEnvelope> ReadAllAsync (
      string sessionId,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

    cancellationToken.ThrowIfCancellationRequested();

    var filePath = GetLogFilePath(sessionId);
    if (!File.Exists(filePath))
    {
      yield break;
    }

    await using var stream = new FileStream(
        filePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite,
        bufferSize: 4096,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    using var reader = new StreamReader(stream);

    while (!reader.EndOfStream)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var line = await reader.ReadLineAsync(cancellationToken);
      if (string.IsNullOrWhiteSpace(line))
      {
        continue;
      }

      var envelope = JsonSerializer.Deserialize<SessionEventEnvelope>(line, SerializerOptions)
        ?? throw new InvalidOperationException($"Session event log line could not be deserialized for session '{sessionId}'.");

      yield return envelope;
    }
  }

  private string GetLogFilePath (string sessionId)
  {
    var logsPath = workspacePaths.ResolveScopedPath(Path.Combine("sessions", "logs"));
    Directory.CreateDirectory(logsPath);
    return Path.Combine(logsPath, $"{sessionId}.events.ndjson");
  }

  private static async Task<long> GetNextSequenceAsync (string filePath, CancellationToken cancellationToken)
  {
    if (!File.Exists(filePath))
    {
      return 1;
    }

    await using var stream = new FileStream(
        filePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite,
        bufferSize: 4096,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    using var reader = new StreamReader(stream);

    SessionEventEnvelope? last = null;
    while (!reader.EndOfStream)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var line = await reader.ReadLineAsync(cancellationToken);
      if (string.IsNullOrWhiteSpace(line))
      {
        continue;
      }

      last = JsonSerializer.Deserialize<SessionEventEnvelope>(line, SerializerOptions)
        ?? throw new InvalidOperationException("Session event log line could not be deserialized while computing sequence.");
    }

    return last is null ? 1 : last.Sequence + 1;
  }
}
