namespace ArkaneSystems.Raven.Core.Bus.Contracts;

// Typed envelope that wraps every bus payload with required metadata.
// Dispatchers and handlers should pass envelopes, not raw payloads.
public sealed record MessageEnvelope<TPayload>(
    MessageMetadata Metadata,
    TPayload Payload)
    where TPayload : notnull;
