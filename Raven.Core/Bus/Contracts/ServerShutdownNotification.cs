namespace ArkaneSystems.Raven.Core.Bus.Contracts;

// Notification broadcast via the session notification channel when the server
// is preparing to shut down or restart. Clients that receive this event should
// display an appropriate warning and stop sending new requests.
//
// This is the notification-channel counterpart of ServerShuttingDown (which
// targets only clients with an active SSE response stream). Both are issued
// during shutdown so that every subscribed client — whether idle or mid-stream
// — is notified.
public sealed record ServerShutdownNotification(bool IsRestart) : IServerNotification;
