namespace ArkaneSystems.Raven.Contracts.Chat;

// Request body for POST /api/chat/sessions.
// Empty for now — the server generates the session ID. Kept as a record
// rather than omitted so the contract can be extended later (e.g. to carry
// a requested persona or initial context) without a breaking change.
public record CreateSessionRequest ();