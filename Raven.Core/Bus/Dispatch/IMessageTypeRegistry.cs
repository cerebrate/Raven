namespace ArkaneSystems.Raven.Core.Bus.Dispatch;

// Registry that maps domain message names to allowed payload CLR types.
public interface IMessageTypeRegistry
{
  void Register (string messageType, params Type[] payloadTypes);

  bool IsAllowed (string messageType, Type payloadType);
}
