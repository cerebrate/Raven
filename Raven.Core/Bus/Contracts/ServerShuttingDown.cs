namespace ArkaneSystems.Raven.Core.Bus.Contracts;

// Broadcast event injected into every active response stream when the server is
// preparing to shut down or restart. Clients that receive this event should
// display an appropriate warning and stop sending new requests.
public sealed record ServerShuttingDown(string ResponseId, bool IsRestart) : IResponseStreamEvent;
