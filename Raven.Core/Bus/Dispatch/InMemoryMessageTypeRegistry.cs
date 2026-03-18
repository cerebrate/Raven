using System.Collections.Concurrent;

namespace ArkaneSystems.Raven.Core.Bus.Dispatch;

// Thread-safe in-memory registry for message type to payload CLR type mapping.
public sealed class InMemoryMessageTypeRegistry : IMessageTypeRegistry
{
  private readonly ConcurrentDictionary<string, ConcurrentDictionary<Type, byte>> _registrations = new(StringComparer.Ordinal);

  public void Register (string messageType, params Type[] payloadTypes)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
    ArgumentNullException.ThrowIfNull(payloadTypes);

    if (payloadTypes.Length == 0)
    {
      throw new ArgumentException("At least one payload type must be provided.", nameof(payloadTypes));
    }

    var allowedTypes = _registrations.GetOrAdd(messageType, _ => new ConcurrentDictionary<Type, byte>());

    foreach (var payloadType in payloadTypes)
    {
      ArgumentNullException.ThrowIfNull(payloadType);
      _ = allowedTypes.TryAdd(payloadType, 0);
    }
  }

  public bool IsAllowed (string messageType, Type payloadType)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
    ArgumentNullException.ThrowIfNull(payloadType);

    return _registrations.TryGetValue(messageType, out var allowedTypes)
           && allowedTypes.ContainsKey(payloadType);
  }
}
