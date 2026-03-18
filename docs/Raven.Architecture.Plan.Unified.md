# Raven Unified Architecture Plan

## Status
Design only. No implementation is included in this plan.

## Scope
This document merges and reconciles:
- `docs/Raven.Architecture.Plan.md`
- `docs/Raven.Future.Suggestions.md`

It provides one consolidated architecture direction and execution backlog.

---

## Goals
- Build **Raven.Core** as the central local .NET host for agent orchestration.
- Build **Raven.Client.Console** as the first thin client over HTTP.
- Keep architecture small at first, but explicitly ready for:
  - multi-client support,
  - streaming-first agent responses,
  - persistent workspace,
  - short/long-term memory,
  - tools/skills and MCP integration,
  - scheduled/heartbeat automation,
  - identity and personalization.

---

## Platform and Runtime Baseline
- Target framework baseline: **.NET 10** (aligned with current repository targeting).
- Host style: **ASP.NET Core + Generic Host**.
- Initial transport: **HTTP + JSON**, with **SSE/streaming HTTP** for incremental responses.

---

## Recommended Solution Shape

### Minimum projects (initial)
1. **Raven.Core**
   - ASP.NET Core host for API, runtime orchestration, memory coordination, tools/MCP policy boundary, background tasks.
2. **Raven.Client.Console**
   - REPL-style client for chat/session operations against Raven.Core.

### Strongly recommended follow-on projects
3. **Raven.Contracts**
   - Shared API contracts and transport DTOs.
4. **Raven.Core.Tests**
   - Unit/integration tests for orchestration and API.
5. **Raven.Client.Console.Tests**
   - Tests for command behavior and rendering.

---

## Primary Architectural Approach

### 1) Raven.Core as a message-driven orchestration host
Use an **internal in-proc message bus + event loop** inside Raven.Core as the execution backbone.

Why:
- Uniform handling for user requests, subagent work, scheduler/heartbeat tasks, and tool callbacks.
- Better modularity and separable handlers.
- Easier evolution toward distributed routing later.

### 2) Streaming as a first-class event model
Streaming is not a side path; it is part of the bus contract.

Recommended response events:
- `ResponseStarted`
- `ResponseDelta`
- `ResponseCompleted`
- `ResponseFailed`

### 3) Keep HTTP API thin
HTTP endpoints should convert requests into internal envelopes, dispatch to application layer/bus, and stream results back.

---

## Cross-Cutting Design Rules
1. **Configuration-driven behavior**
   - model/deployment, MCP servers, features, policies, retention, and scheduling all via config.
2. **SDK isolation**
   - Foundry/Azure/MCP SDK types remain behind adapters/interfaces.
3. **Streaming-first contracts**
   - all chat flows must support incremental output.
4. **Policy-gated tool execution**
   - especially mutating, sensitive, or external operations.
5. **Memory is layered**
   - scratchpad, episodic/session summaries, semantic/user facts.
6. **Observability from day one**
   - correlation IDs, structured logs, traces, metrics.
7. **Reliable operations**
   - retries with jitter, idempotency where possible, dead-letter handling.

---

## Core Runtime Contracts (Recommended)

### Message Envelope
Minimum metadata:
- `MessageId`
- `CorrelationId`
- `CausationId`
- `SessionId`
- `UserId`
- `Type`
- `Priority`
- timestamps

### Reliability
- Bounded queues + backpressure.
- Handler idempotency where practical.
- Dead-letter capture with failure reason and retry metadata.

---

## Raven.Core Responsibilities (Unified)
1. Client communication (chat/session APIs, streaming)
2. Agent runtime orchestration (prompt/context/tools/memory assembly)
3. Memory orchestration (short-term + long-term)
4. Tool/skill registry and execution policy
5. MCP gateway and server/tool governance
6. Periodic jobs + heartbeat automation
7. Session lifecycle management
8. Identity/persona/profile management
9. Operational concerns (logging, tracing, metrics, health, config, secrets)

---

## Raven.Core Internal Architecture

### Recommended internal slices
- **Host** (startup, DI, config, logging)
- **Api** (chat/session/health endpoints)
- **Application** (use-cases, commands/queries)
- **Bus** (envelopes, dispatcher, handlers, retries, DLQ)
- **AgentRuntime** (Foundry adapters, prompting, streaming, tool loops)
- **Memory** (stores, consolidation, retrieval)
- **Skills** (local tools, manifests, policy/risk metadata)
- **Mcp** (registry, client adapters, auth/approval/policy)
- **Jobs** (scheduled jobs + heartbeat execution)
- **Sessions** (state snapshots, retention, import/export)
- **Identity** (AGENT/SOUL/USER profile config)
- **Infrastructure** (persistence, filesystem, telemetry, clock)
- **Contracts** (DTOs until dedicated Raven.Contracts exists)

---

## Unified Functional Design

### Bus and Event Loop
- In-proc first, transport abstraction for future distributed mode.
- Queue budget protections and visibility metrics.
- Streaming event fan-out to clients while preserving order/correlation.

### Workspace
Recommended structure:
- `/sessions`
- `/memory`
- `/heartbeat`
- `/artifacts`
- `/audit`

Operational requirements:
- atomic writes for critical files,
- scoped path access policies,
- retention and cleanup,
- startup integrity checks.

### Memory
Memory tiers:
- **Scratchpad** (short-term, session-scoped)
- **Episodic** (session summaries)
- **Semantic** (durable facts/preferences)

Minimum metadata per memory item:
- source/provenance,
- confidence,
- timestamps,
- last-validated,
- conflict state.

### Tools and Skills
- Unified manifest for internal + MCP tools.
- Registry supports search by tags/capabilities/risk/cost.
- Side-effect declaration required (read-only vs mutating).
- Health checks and circuit-break behavior.
- Approval flow for high-risk actions.

### Clients
- Clients are adapters over common session/event API.
- Capability negotiation (streaming, approvals, attachments).
- Identity boundary rules for single-user vs multi-user channels.
- Mention-directed routing for channel integrations.

### Heartbeat and Scheduling
- Separate classes of background tasks: health probes, maintenance, reminders.
- Persist state: `nextRun`, `lastRun`, `lastStatus`.
- Task budgets/timeouts to protect interactive latency.
- User-visible activity trail for all automatic task changes.

### Session Management
- Append-only event log per session + periodic snapshots.
- Rejoin support and optional branch/fork sessions.
- Export/import support.
- Explicit deletion semantics (soft/hard/delete-older-than-X) and retention policy.

### Identity and Personalization
- Structured profile sections: **AGENT**, **SOUL**, **USER**.
- Versioned profile documents with diff/rollback.
- Explicitly separate persona controls from non-overridable safety policies.
- Trust levels for user facts (explicit vs inferred).

---

## API Surface (First Increment)
Recommended initial endpoints:
- `POST /api/chat/sessions`
- `POST /api/chat/sessions/{sessionId}/messages`
- `POST /api/chat/sessions/{sessionId}/messages/stream`
- `GET /api/chat/sessions/{sessionId}`
- `DELETE /api/chat/sessions/{sessionId}`

Planned follow-on groups:
- `/api/tools` (registry, capability discovery)
- `/api/heartbeat` (list/edit jobs)
- `/api/memory` (inspect summaries/facts)
- `/api/profile` (AGENT/SOUL/USER management)

---

## Foundry Integration Guidance
- Use Microsoft Agent Framework / Foundry integration via `AIProjectClient`.
- Keep model/deployment configuration external.
- Default interactive model can be cost/latency optimized (configurable).
- Do not leak SDK-specific types outside adapter layer.

Suggested interfaces:
- `IAgentConversationService`
- `IAgentResponseStreamer`
- `IAgentToolCoordinator`
- `IAgentMemoryAssembler`

---

## Persistence and Data Strategy
- First structured persistence recommendation: **SQLite + EF Core**.
- Keep provider abstractions for later replacement/expansion.

Suggested abstractions:
- `IMemoryStore`
- `IConversationStore`
- `IConversationSummaryStore`
- `IUserProfileStore`
- `ISessionStore`

Future expansion:
- vector store,
- semantic retrieval,
- external DB/search systems.

---

## Security, Governance, and Audit
- Least-privilege file/tool access.
- Explicit policy checks for all tool invocations.
- Approval requirement for configured high-risk operations.
- Immutable-enough audit trail for sensitive actions.
- Secret isolation and secure configuration handling.

---

## Observability and Operations
Minimum telemetry set:
- Structured logs with correlation IDs.
- Traces across request -> bus -> tool -> response.
- Metrics for latency, queue depth, error rates, retries, tool health.
- Health checks for host dependencies and tool registry state.

---

## Recommended Libraries (Consolidated)

### Host/API
- ASP.NET Core (Minimal APIs)
- Microsoft.Extensions.Hosting
- Microsoft.Extensions.Options
- Microsoft.Extensions.Http

### Foundry/Agent Integration
- Azure.Identity
- Microsoft.Agents.AI.AzureAI (prerelease as required)
- Microsoft.Agents.AI.Workflows (when multi-agent/workflow is needed)

### Persistence
- Microsoft.EntityFrameworkCore.Sqlite
- Microsoft.EntityFrameworkCore.Design

### Resilience
- Polly and/or built-in HttpClient resilience handlers

### Logging/Telemetry
- Serilog.AspNetCore
- Serilog.Sinks.Console
- OpenTelemetry.Extensions.Hosting
- Azure.Monitor.OpenTelemetry.AspNetCore (optional)

### MCP
- ModelContextProtocol (prerelease)

### Validation
- FluentValidation

### Console Client
- System.CommandLine (optional)
- Spectre.Console (optional)

### Testing
- xUnit
- FluentAssertions
- Microsoft.AspNetCore.Mvc.Testing
- NSubstitute or Moq

---

## Suggested Folder Layout

### Raven.Core
```text
Raven.Core/
  Api/
    Endpoints/
    Models/
  Application/
    Chat/
    Sessions/
    Tools/
    Jobs/
  Bus/
    Contracts/
    Dispatch/
    Handlers/
  AgentRuntime/
    Foundry/
    Prompting/
    Streaming/
    Tooling/
  Memory/
    Abstractions/
    Stores/
    Consolidation/
    Retrieval/
  Mcp/
    Clients/
    Registry/
    Policies/
    Approval/
  Skills/
    Abstractions/
    BuiltIn/
    Registry/
  Sessions/
  Identity/
  Jobs/
    Heartbeat/
    Maintenance/
  Infrastructure/
    Persistence/
    Configuration/
    Telemetry/
    Filesystem/
  Contracts/
  Program.cs
```

### Raven.Client.Console
```text
Raven.Client.Console/
  Commands/
  Rendering/
  Services/
  Models/
  Program.cs
```

---

## Prioritized Architecture Backlog

### Priority Legend
- **P0**: Foundational and blocking
- **P1**: High-value core capability
- **P2**: Advanced capability/hardening

### Epic 1 (P0): Core Message Bus and Streaming Runtime
**Goal:** Stable in-proc event loop and streaming event flow.

Tasks:
1. Define envelope/event taxonomy.
2. Implement dispatcher with bounded queues/backpressure.
3. Implement `Started/Delta/Completed/Failed` streaming path.
4. Propagate correlation/session/user metadata.
5. Add dead-letter handling.

Acceptance criteria:
- Typed envelopes for all runtime requests.
- Incremental stream delivery without loop blocking.
- No unbounded queue growth under load.
- Failures captured with retry metadata.
- End-to-end trace correlation available.

### Epic 2 (P0): Workspace Layout, Safety, and Persistence
**Goal:** Durable workspace with scoped access and safe writes.

Tasks:
1. Implement workspace directory contract.
2. Add path-policy guardrails.
3. Add atomic write utility.
4. Implement retention cleanup.
5. Add integrity/recovery startup checks.

Acceptance criteria:
- Workspace auto-initializes correctly.
- Unauthorized paths are blocked.
- Crash-safe write behavior for critical files.
- Retention policy executes as configured.
- Integrity checks report actionable issues.

### Epic 3 (P1): Session Engine and Lifecycle Management
**Goal:** Isolated multi-session operation with replay/rejoin controls.

Tasks:
1. Append-only per-session event logs.
2. Session snapshots.
3. Session lifecycle APIs.
4. Retention/delete policy support.
5. Export/import support.

Acceptance criteria:
- No context leakage between sessions.
- Rejoin restores expected state.
- Deletion policy is enforceable/auditable.
- Export/import replay is consistent.

### Epic 4 (P1): Short-Term + Long-Term Memory Pipeline
**Goal:** Practical, explainable memory continuity.

Tasks:
1. Session scratchpad storage.
2. End-of-session consolidation.
3. Semantic memory schema with provenance/confidence.
4. Retrieval ranking.
5. Conflict resolution/deduplication.

Acceptance criteria:
- Scratchpad available during session.
- Deterministic consolidation on close.
- Long-term entries include provenance/confidence.
- Retrieval explains why memory was selected.
- Contradictions are flagged/handled by policy.

### Epic 5 (P1): Tool/Skill Registry, Discovery, and Permissions
**Goal:** One unified, policy-aware tool interface.

Tasks:
1. Unified manifest schema.
2. Runtime registry query/search.
3. Approval flow integration.
4. Tool health/circuit-break behavior.
5. Invocation audit capture.

Acceptance criteria:
- Runtime tool discovery works for internal + MCP.
- High-risk actions trigger approvals when required.
- Unhealthy tools are safely bypassed/disabled.
- Audit trail captures actor/action/outcome.

### Epic 6 (P1): Client Adapter Layer and Identity Boundaries
**Goal:** Consistent multi-client behavior with correct attribution.

Tasks:
1. Adapter contract.
2. Identity mapping strategy.
3. Intent classification (`to-agent` vs `in-presence`).
4. Mention-based routing for channel clients.
5. Capability negotiation.

Acceptance criteria:
- Common adapter model for CLI/web/channel.
- Accurate user attribution across events.
- Non-addressed messages do not trigger actions.
- Streaming/approval behavior consistent by client capability.

### Epic 7 (P2): Heartbeat and Background Automation
**Goal:** Transparent periodic automation without UX degradation.

Tasks:
1. Heartbeat scheduler on bus.
2. Health/maintenance/reminder task classes.
3. Persisted schedule state.
4. Per-task budget/timeout.
5. User-visible management and activity view.

Acceptance criteria:
- Heartbeat survives restart.
- Foreground work is not starved.
- Users can inspect/manage heartbeat tasks.
- Agent surfaces automated task changes.

### Epic 8 (P2): Identity and Personalization Profiles
**Goal:** Versioned AGENT/SOUL/USER configuration with safe boundaries.

Tasks:
1. Profile schema/storage.
2. Versioning + rollback.
3. Trust levels (explicit/inferred).
4. Preview mode for profile changes.
5. Safety override prevention.

Acceptance criteria:
- Editable versioned profiles.
- Distinct explicit vs inferred user facts.
- Preview demonstrates behavior impact.
- Safety policies remain non-overridable.

### Epic 9 (P2): Observability, Reliability, and Governance Hardening
**Goal:** Production-ready diagnosability and control.

Tasks:
1. End-to-end logs/traces.
2. Runtime metrics and dashboards.
3. Retry/idempotency policy implementation.
4. Governance policy engine.
5. Failure injection/recovery tests.

Acceptance criteria:
- Correlated telemetry across critical flows.
- Actionable latency/error visibility.
- Bounded deterministic retries.
- Policy-enforced gating for high-risk actions.
- Recovery behavior validated by tests.

---

## Delivery Sequence
1. **Phase A (P0):** Epic 1 + Epic 2
2. **Phase B (P1 Core):** Epic 3 + Epic 5
3. **Phase C (P1 Intelligence):** Epic 4 + Epic 6
4. **Phase D (P2 Features):** Epic 7 + Epic 8
5. **Phase E (P2 Hardening):** Epic 9

---

## Definition of Done (Per Epic)
- Design doc approved (scope/interfaces/risks/rollout).
- Implementation complete with integration tests.
- Telemetry added for success and failure paths.
- User/developer documentation updated.
- Backward compatibility and migration plan documented when relevant.
