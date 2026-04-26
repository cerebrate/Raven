namespace ArkaneSystems.Raven.Core.Bus.Contracts;

// Server notification plus metadata for correlation and tracing.
// Analogous to ResponseStreamEventEnvelope, but for the session-level
// notification channel rather than per-response SSE streams.
public sealed record ServerNotificationEnvelope(
    MessageMetadata Metadata,
    IServerNotification Notification);
