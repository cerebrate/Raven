namespace ArkaneSystems.Raven.Client.Console.Services;

// A single parsed SSE event received from the server notification channel
// (GET /api/chat/sessions/{id}/notifications).
//
// Known event types:
//   "server_shutdown"  — data is "restart" or "shutdown"
// Additional event types may be added in future server versions; clients should
// ignore unknown types gracefully.
public sealed record ServerNotification(string EventType, string Data);
