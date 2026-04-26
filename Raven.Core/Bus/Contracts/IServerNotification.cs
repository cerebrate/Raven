namespace ArkaneSystems.Raven.Core.Bus.Contracts;

// Marker contract for server-initiated push notifications sent through the
// per-session notification channel. Unlike IResponseStreamEvent (which is tied
// to a specific streaming response and identified by ResponseId), server
// notifications are session-scoped and can arrive at any time — not only while
// a chat response is in progress.
//
// The notification channel (GET /api/chat/sessions/{id}/notifications) is a
// long-lived SSE connection the client keeps open indefinitely. Any feature
// that needs to push information to an idle client (shutdown/restart warnings,
// memory updates, background task results, heartbeat pings, etc.) should
// implement IServerNotification and publish via ISessionNotificationHub.
public interface IServerNotification;
