namespace ArkaneSystems.Raven.Core.Bus.Contracts;

// Priority controls message scheduling preference once the dispatcher is introduced.
// The contract exists now so producers can stamp intent consistently.
public enum MessagePriority
{
  Low = 0,
  Normal = 1,
  High = 2,
  Critical = 3
}
