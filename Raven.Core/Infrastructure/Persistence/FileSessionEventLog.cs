#region header

// Raven.Core - FileSessionEventLog.cs
// 
// Alistair J. R. Young
// Arkane Systems
// 
// Copyright Arkane Systems 2012-2026.  All rights reserved.
// 
// Created: 2026-04-25 2:17 PM

#endregion

#region using

using ArkaneSystems.Raven.Core.Application.Sessions;
using ArkaneSystems.Raven.Core.Infrastructure.Filesystem;
using JetBrains.Annotations;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;

#endregion

namespace ArkaneSystems.Raven.Core.Infrastructure.Persistence;

public sealed class FileSessionEventLog (IWorkspacePaths workspacePaths) : ISessionEventLog
{
  private static readonly JsonSerializerOptions SerializerOptions = new (JsonSerializerDefaults.Web);

  private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new (StringComparer.Ordinal);

  /// <summary>
  ///   Appends a single event as a JSON line to the per-session NDJSON log file.
  /// </summary>
  public async Task<SessionEventEnvelope> AppendAsync (string            sessionId,
                                                       string            eventType,
                                                       object            payload,
                                                       string?           correlationId     = null,
                                                       string?           userId            = null,
                                                       CancellationToken cancellationToken = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace (sessionId);
    ArgumentException.ThrowIfNullOrWhiteSpace (eventType);
    ArgumentNullException.ThrowIfNull (payload);

    cancellationToken.ThrowIfCancellationRequested ();

    SemaphoreSlim gate =
      this._sessionLocks.GetOrAdd (key: sessionId, valueFactory: static _ => new SemaphoreSlim (initialCount: 1, maxCount: 1));
    await gate.WaitAsync (cancellationToken);

    try
    {
      string filePath = this.GetLogFilePath (sessionId);
      long   sequence = await GetNextSequenceAsync (filePath: filePath, cancellationToken: cancellationToken);

      SessionEventEnvelope envelope = new SessionEventEnvelope (EventId: Guid.NewGuid ().ToString (),
                                                                SessionId: sessionId,
                                                                Sequence: sequence,
                                                                EventType: eventType,
                                                                OccurredAtUtc: DateTimeOffset.UtcNow,
                                                                CorrelationId: correlationId,
                                                                UserId: userId,
                                                                SchemaVersion: 1,
                                                                Payload: payload);

      string line = JsonSerializer.Serialize (value: envelope, options: SerializerOptions);

      await using FileStream stream = new FileStream (path: filePath,
                                                      mode: FileMode.Append,
                                                      access: FileAccess.Write,
                                                      share: FileShare.Read,
                                                      bufferSize: 4096,
                                                      options: FileOptions.Asynchronous | FileOptions.WriteThrough);

      await using StreamWriter writer = new StreamWriter (stream);
      await writer.WriteLineAsync (line);
      await writer.FlushAsync (cancellationToken);

      return envelope;
    }
    finally
    {
      _ = gate.Release ();
    }
  }

  /// <summary>
  ///   Reads all events in append order from the per-session NDJSON log file.
  /// </summary>
  public async IAsyncEnumerable<SessionEventEnvelope> ReadAllAsync (string sessionId,
                                                                    [EnumeratorCancellation] CancellationToken cancellationToken =
                                                                      default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace (sessionId);

    cancellationToken.ThrowIfCancellationRequested ();

    string filePath = this.GetLogFilePath (sessionId);

    if (!File.Exists (filePath))
    {
      yield break;
    }

    await using FileStream stream = new FileStream (path: filePath,
                                                    mode: FileMode.Open,
                                                    access: FileAccess.Read,
                                                    share: FileShare.ReadWrite,
                                                    bufferSize: 4096,
                                                    options: FileOptions.Asynchronous | FileOptions.SequentialScan);

    using StreamReader reader = new StreamReader (stream);

    string? line;

    while ((line = await reader.ReadLineAsync (cancellationToken)) is not null)
    {
      cancellationToken.ThrowIfCancellationRequested ();

      if (string.IsNullOrWhiteSpace (line))
      {
        continue;
      }

      SessionEventEnvelope envelope = JsonSerializer.Deserialize<SessionEventEnvelope> (json: line, options: SerializerOptions) ??
                                      throw new
                                        InvalidOperationException ($"Session event log line could not be deserialized for session '{sessionId}'.");

      yield return envelope;
    }
  }

  private string GetLogFilePath (string sessionId)
  {
    string logsPath = workspacePaths.ResolveScopedPath (Path.Combine (path1: "sessions", path2: "logs"));
    _ = Directory.CreateDirectory (logsPath);

    return Path.Combine (path1: logsPath, path2: $"{sessionId}.events.ndjson");
  }

  private static async Task<long> GetNextSequenceAsync (string filePath, CancellationToken cancellationToken)
  {
    if (!File.Exists (filePath))
    {
      return 1;
    }

    await using FileStream stream = new FileStream (path: filePath,
                                                    mode: FileMode.Open,
                                                    access: FileAccess.Read,
                                                    share: FileShare.ReadWrite,
                                                    bufferSize: 4096,
                                                    options: FileOptions.Asynchronous | FileOptions.SequentialScan);

    using StreamReader reader = new StreamReader (stream);

    SessionEventEnvelope? last = null;
    string?               line;

    while ((line = await reader.ReadLineAsync (cancellationToken)) is not null)
    {
      cancellationToken.ThrowIfCancellationRequested ();

      if (string.IsNullOrWhiteSpace (line))
      {
        continue;
      }

      last = JsonSerializer.Deserialize<SessionEventEnvelope> (json: line, options: SerializerOptions) ??
             throw new InvalidOperationException ("Session event log line could not be deserialized while computing sequence.");
    }

    return last is null ? 1 : last.Sequence + 1;
  }
}