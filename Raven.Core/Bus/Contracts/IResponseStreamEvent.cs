namespace ArkaneSystems.Raven.Core.Bus.Contracts;

// Marker contract for ordered streaming response events.
public interface IResponseStreamEvent
{
  string ResponseId { get; }
}
