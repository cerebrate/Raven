namespace ArkaneSystems.Raven.Contracts.Admin;

// Response body returned by POST /api/admin/shutdown and POST /api/admin/restart.
// Confirms that the command was accepted and describes what will happen next.
public sealed record AdminCommandResponse(string Message);
