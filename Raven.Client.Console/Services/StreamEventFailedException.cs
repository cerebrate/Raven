namespace ArkaneSystems.Raven.Client.Console.Services;

// Thrown when the server emits an SSE failed event.
public sealed class StreamEventFailedException(
    string message,
    string? code,
    bool isRetryable)
    : InvalidOperationException(message)
{
  public string? Code { get; } = code;

  public bool IsRetryable { get; } = isRetryable;
}
