using ArkaneSystems.Raven.Core.Bus.Contracts;

namespace ArkaneSystems.Raven.Core.Bus.Dispatch;

// In-memory pub/sub hub for server-initiated notifications keyed by SessionId.
//
// Each client that subscribes to GET /api/chat/sessions/{id}/notifications gets
// a dedicated Channel via TrySubscribe. The server can push a notification to a
// single session (PublishToSessionAsync) or broadcast to all subscribed sessions
// (BroadcastAsync). The channel stays open until the client disconnects or the
// server calls Complete.
//
// The design is deliberately symmetric with IResponseStreamEventHub so future
// routing/delivery guarantees can be added uniformly across both hubs.
public interface ISessionNotificationHub
{
  // Creates a notification channel for the given session.
  // Returns false if a subscription already exists for this session (the caller
  // should return 409 Conflict and let the client close its old connection first).
  bool TrySubscribe (string sessionId);

  // Reads notifications published to the given session.
  // Completes when Complete(sessionId) is called or the CancellationToken fires.
  // Removes the subscription from the hub on exit.
  IAsyncEnumerable<ServerNotificationEnvelope> ReadAllAsync (string sessionId, CancellationToken cancellationToken);

  // Sends a notification to a single subscribed session.
  // Silent no-op if the session is not subscribed (client may have disconnected).
  ValueTask PublishToSessionAsync (string sessionId, ServerNotificationEnvelope envelope, CancellationToken cancellationToken);

  // Sends a notification to every currently subscribed session.
  // Best-effort: a failure on one channel must not prevent others from receiving it.
  ValueTask BroadcastAsync (ServerNotificationEnvelope envelope, CancellationToken cancellationToken);

  // Closes the notification channel for the given session, completing its reader.
  void Complete (string sessionId);

  // Returns a snapshot of all currently subscribed session IDs.
  // Used by ShutdownCoordinator to broadcast before stopping.
  IReadOnlyCollection<string> GetSubscribedSessionIds ();
}
