# Raven.Core DI Registration Map (Phase A1)

This map captures the current service registration layout in `Raven.Core/Program.cs` after Phase A1 preflight work.

## Purpose
- Make lifetimes explicit for architecture review.
- Provide a single reference before further bus/runtime refactors.
- Reduce accidental lifetime mismatches across Core, Bus, and streaming components.

---

## Registration Summary

### Configuration / Options
- `IOptions<FoundryOptions>` via `Configure<FoundryOptions>`
  - **Source:** `Foundry` config section
- `IOptions<BusDispatchOptions>` via `Configure<BusDispatchOptions>`
  - **Source:** `Raven:Bus` config section

### Workspace / Filesystem
- `IWorkspacePaths -> WorkspacePaths`
  - **Lifetime:** Singleton
  - **Notes:** Workspace root resolved at startup; structure and integrity checked before host run.
  - **Startup telemetry:**
    - Initialization checkpoint logs created/existing/total workspace directories.
    - Integrity checkpoint logs health, missing-directory count, and write-probe success.
    - Startup fails fast with a detailed integrity error message that includes missing directory paths and probe failure details.

### Persistence
- `IDbContextFactory<RavenDbContext>` via `AddDbContextFactory`
  - **Lifetime:** Factory registration (singleton factory behavior)
  - **Notes:** SQLite connection targets workspace DB path; startup migration check + apply when pending.

### Agent Runtime
- `IAgentConversationService -> FoundryAgentConversationService`
  - **Lifetime:** Singleton
  - **Notes:** Holds long-lived Foundry agent/session map.

### Application Layer
- `ISessionStore -> SqliteSessionStore`
  - **Lifetime:** Scoped
- `IChatApplicationService -> ChatApplicationService`
  - **Lifetime:** Scoped
- `IChatStreamBroker -> ChatStreamBroker`
  - **Lifetime:** Scoped

### Streaming Hub / Bus Infrastructure
- `IResponseStreamEventHub -> InMemoryResponseStreamEventHub`
  - **Lifetime:** Singleton
- `IMessageTypeRegistry -> InMemoryMessageTypeRegistry` (pre-seeded)
  - **Lifetime:** Singleton
  - **Seeded message types:**
    - `chat.response.started.v1` -> `ResponseStreamEventEnvelope`
    - `chat.response.delta.v1` -> `ResponseStreamEventEnvelope`
    - `chat.response.completed.v1` -> `ResponseStreamEventEnvelope`
    - `chat.response.failed.v1` -> `ResponseStreamEventEnvelope`
- `IMessageHandler<ResponseStreamEventEnvelope> -> ResponseStreamEventForwardingHandler`
  - **Lifetime:** Singleton
- `IDeadLetterSink -> LoggingDeadLetterSink`
  - **Lifetime:** Singleton
- `InProcMessageBus`
  - **Lifetime:** Singleton
- `IMessageBus -> InProcMessageBus`
  - **Lifetime:** Singleton mapping
- Hosted service registration -> `InProcMessageBus`
  - **Lifetime:** Host-managed background service

---

## Current Lifetime Rationale

- **Singletons**
  - Bus core, message registry, dead-letter sink, stream hub, and Foundry runtime are process-wide coordination components.
- **Scoped application services**
  - Chat/session orchestration remains request-scoped to align with endpoint usage and persistence patterns.
- **DbContext factory pattern**
  - Session store opens short-lived contexts per operation, avoiding leaked context state across requests.

---

## Test Host Overrides (Integration)
In `Raven.Core.Tests/Integration/TestHost/RavenCoreWebAppFactory.cs`:
- `IAgentConversationService` overridden with `FakeAgentConversationService` (singleton)
- `ISessionStore` overridden with `InMemorySessionStore` (singleton)

This preserves endpoint semantics while removing external runtime and SQLite dependencies for integration tests.

---

## Next DI Map Checkpoint
After Epic 1 hardening, revisit this map to confirm:
- additional handler registrations,
- message producer registrations,
- any transition from scoped to singleton/transient for application orchestration boundaries.
