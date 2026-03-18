namespace ArkaneSystems.Raven.Core.Bus.Dispatch;

// Configuration for bounded in-process dispatcher queues.
public sealed class BusDispatchOptions
{
  public const string SectionName = "Raven:Bus";

  // Bounded capacity protects process memory under burst load.
  public int ChannelCapacity { get; set; } = 512;
}
