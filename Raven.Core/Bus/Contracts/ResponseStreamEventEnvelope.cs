namespace ArkaneSystems.Raven.Core.Bus.Contracts;

// Response stream event plus metadata for correlation and tracing.
public sealed record ResponseStreamEventEnvelope(
    MessageMetadata Metadata,
    IResponseStreamEvent Event);
