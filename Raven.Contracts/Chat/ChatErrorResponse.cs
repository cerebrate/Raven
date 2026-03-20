namespace ArkaneSystems.Raven.Contracts.Chat;

// Error payload returned by chat endpoints for machine-readable failure states.
public record ChatErrorResponse(string Code);
