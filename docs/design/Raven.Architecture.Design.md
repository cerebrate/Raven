# Raven Architecture & Design (Consolidated)

## Status
Architecture/design direction and implementation-aligned reference for Raven.

This document consolidates and replaces:
- `Raven.Architecture.Plan.md`
- `Raven.Architecture.Plan.Unified.md`
- `Raven.Architecture.Backlog.GitHub.md`
- `Raven.Core.DI.RegistrationMap.md`
- `Raven.Future.Suggestions.md`

---

## 1) Product Intent and Scope

Raven is a .NET 10 agent platform with two primary components:
- **Raven.Core**: ASP.NET Core host for API, agent runtime orchestration, persistence, tool policy boundaries, and background processing.
- **Raven.Client.Console**: REPL-style thin client over HTTP/SSE.

Architecture is intentionally small at first, but explicitly designed for:
- streaming-first responses,
- durable workspace and persistence,
- multi-session lifecycle controls,
- tool/skill + MCP integration,
- short/long-term memory,
- heartbeat/background automation,
- identity and personalization,
- production hardening and governance.

---

## 2) Platform and Runtime Baseline

- Runtime baseline: **.NET 10**
- Host style: **ASP.NET Core + Generic Host**
- Initial transport: **HTTP + JSON**, with **SSE/streaming HTTP** for incremental output

### Why this host/transport baseline
- ASP.NET Core host gives DI/config/logging/health/background services by default.
- HTTP + SSE keeps the first client simple while preserving streaming UX.
- Leaves room to add richer transports later (SignalR/gRPC) without changing core orchestration boundaries.

---

## 3) Current Runtime Baseline (Implemented)

### Current core flow
- Client creates `sessionId`.
- Core maps client-visible `sessionId` to internal `conversationId`.
- Requests include correlation metadata.
- Streaming event progression:
  - `chat.response.started.v1`
  - `chat.response.delta.v1`
  - `chat.response.completed.v1`
  - `chat.response.failed.v1`

### Workspace
Workspace root resolution precedence:
1. `Raven:Workspace:RootPath`
2. `RAVEN_WORKSPACE_ROOT`
3. container default `/data/workspace`
4. local default:
   - Windows: `%LOCALAPPDATA%\Arkane Systems\Raven\Workspace`
   - Linux/macOS: `~/.local/share/Arkane Systems/Raven/workspace`

Workspace v1 structure:
```text
{workspace-root}/
  sessions/
	db/
	  raven.db
	logs/
	snapshots/
  memory/
  heartbeat/
  artifacts/
  audit/
  config/
  tmp/
```

Startup behavior:
- workspace structure initialization
- startup integrity checks + write probe
- startup telemetry checkpoints for initialization and integrity

### Persistence
- SQLite + EF Core (`RavenDbContext`)
- session DB at `sessions/db/raven.db`
- migrations auto-applied at startup

### Message bus and stream infrastructure
- in-proc bounded channel dispatcher
- dead-letter sink
- message type registry + contract checks
- in-memory stream hub for SSE fan-out

### Session append-only event log
Per-session append-only NDJSON stream:
- `{workspace}/sessions/logs/{sessionId}.events.ndjson`

Event envelope fields:
- `eventId`
- `sessionId`
- `sequence`
- `eventType`
- `occurredAtUtc`
- `correlationId`
- `userId`
- `schemaVersion`
- `payload`

Current event types include:
- `session.created.v1`
- `session.deleted.v1`
- `chat.message.sent.v1`
- `chat.message.failed.v1`
- `chat.stream.started.v1`
- `chat.stream.completed.v1`
- `chat.stream.failed.v1`

### DI snapshot (current)
- `IWorkspacePaths -> WorkspacePaths` (Singleton)
- `IAgentConversationService -> FoundryAgentConversationService` (Singleton)
- `ISessionStore -> SqliteSessionStore` (Scoped)
- `ISessionEventLog -> FileSessionEventLog` (Singleton)
- `IChatApplicationService -> ChatApplicationService` (Scoped)
- `IChatStreamBroker -> ChatStreamBroker` (Scoped)
- `IResponseStreamEventHub -> InMemoryResponseStreamEventHub` (Singleton)
- `IMessageTypeRegistry -> InMemoryMessageTypeRegistry` (Singleton)
- `IMessageHandler<ResponseStreamEventEnvelope> -> ResponseStreamEventForwardingHandler` (Singleton)
- `IDeadLetterSink -> LoggingDeadLetterSink` (Singleton)
- `InProcMessageBus` + `IMessageBus` mapping (Singleton)

---

## 4) Recommended Solution Shape

### Minimum projects
1. **Raven.Core**
2. **Raven.Client.Console**

### Strongly recommended follow-on projects
3. **Raven.Contracts**
4. **Raven.Core.Tests**
5. **Raven.Client.Console.Tests**

Design rule: keep clients thin; core orchestration/business behavior belongs in Raven.Core.

---

## 5) Primary Architectural Approach

### 5.1 Message-driven host core
Use an internal message bus/event loop as the Raven.Core execution backbone.

Why:
- uniform handling for user requests, tool callbacks, scheduler/heartbeat tasks, and subagent work
- better modularity and handler separation
- easier future evolution toward distributed routing

### 5.2 Streaming as first-class event model
Streaming is part of the core contract, not a side path.

Canonical streaming events:
- `ResponseStarted`
- `ResponseDelta`
- `ResponseCompleted`
- `ResponseFailed`

### 5.3 Thin API surface
HTTP endpoints should remain transport adapters that:
- validate and map input,
- dispatch to application/bus/runtime services,
- stream/return output.

---

## 6) Cross-Cutting Design Rules

1. **Configuration-driven behavior**
2. **SDK isolation behind adapters**
3. **Streaming-first contracts**
4. **Policy-gated tools**
5. **Layered memory model**
6. **Correlated telemetry end-to-end**
7. **Reliability controls** (bounded queues, retries, idempotency, dead-letter behavior)

---

## 7) Core Runtime Contracts

### Message envelope (minimum metadata)
- `MessageId`
- `CorrelationId`
- `CausationId`
- `SessionId`
- `UserId`
- `Type`
- `Priority`
- timestamps

### Reliability contract
- bounded queues and backpressure
- handler idempotency where practical
- dead-letter capture with failure/retry context

---

## 8) Raven.Core Responsibilities

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

## 9) Internal Architecture (Target Slices)

```text
Raven.Core/
  Api/
  Application/
  Bus/
  AgentRuntime/
  Memory/
  Skills/
  Mcp/
  Sessions/
  Identity/
  Jobs/
  Infrastructure/
```

Suggested key abstractions:
- Agent runtime:
  - `IAgentConversationService`
  - `IAgentResponseStreamer`
  - `IAgentToolCoordinator`
  - `IAgentMemoryAssembler`
- Memory:
  - `IMemoryStore`
  - `IConversationStore`
  - `IConversationSummaryStore`
  - `IUserProfileStore`
- MCP:
  - `IMcpGateway`
  - `IMcpServerRegistry`
  - `IMcpApprovalService`
  - `IMcpToolPolicy`

---

## 10) Functional Design Decisions and Directions

### Bus and event loop
- in-proc first, with abstraction for later distributed mode
- queue budget protections and visibility metrics
- ordered streaming fan-out with correlation propagation

### Workspace durability and safety
- scoped path resolution + traversal prevention
- atomic writes for critical files
- startup integrity checks
- retention/cleanup for non-durable artifacts

### Workspace-owned configuration (future direction)
Use a two-stage bootstrap:
1. **Bootstrap config**: minimal host config to resolve workspace root + logging baseline
2. **Workspace config**: load app config from `{workspace-root}/config/...`

Target precedence (high -> low):
1. command line
2. environment
3. workspace `appsettings.{Environment}.json`
4. workspace `appsettings.json`
5. built-in defaults

### Memory tiers
- scratchpad (session-scoped)
- episodic (session summaries)
- semantic (durable facts/preferences)

Memory metadata should include provenance/confidence/timestamps/validation state/conflict state.

### Tools, skills, and MCP
- unified tool manifest and registry
- capability/risk/cost tags
- side-effect declaration (`read-only` vs `mutating`)
- approval gates for high-risk actions
- health checks + circuit-break behavior
- audit trail for invocation intent/input summary/outcome

### MCP strategy
Support both conceptual modes:
1. **Host-managed MCP access** (strongest local approval/audit/policy control)
2. **Foundry-managed MCP tool access** (leaning into Foundry-native orchestration)

### Clients
- adapters over common session/event APIs
- capability negotiation (streaming/approvals/attachments)
- identity boundaries for single-user and multi-user channel contexts
- mention-directed routing for channel integrations

### Heartbeat and scheduling
- task classes: health, maintenance, reminders
- persisted schedule state (`nextRun`, `lastRun`, `lastStatus`)
- per-task budgets/timeouts to protect interactive latency
- user-visible automation activity trail

Scheduling recommendation:
- start with `BackgroundService`/`IHostedService`
- add Quartz-style scheduler only when advanced schedule/retry orchestration is required

### Session lifecycle
- append-only event logs + snapshots (target)
- rejoin/replay/export/import (target)
- retention/deletion policy enforcement
- current stale mapping policy: **invalidate-and-recover**
- planned evolution: replay-based restore when prerequisites are in place

### Identity and personalization
- AGENT / SOUL / USER profile sections
- versioned profile docs with diff/rollback
- explicit trust levels (explicit vs inferred user facts)
- non-overridable safety boundaries

---

## 11) API Surface

Implemented baseline:
- `POST /api/chat/sessions`
- `POST /api/chat/sessions/{sessionId}/messages`
- `POST /api/chat/sessions/{sessionId}/messages/stream`
- `GET /api/chat/sessions/{sessionId}`
- `DELETE /api/chat/sessions/{sessionId}`

Planned follow-on groups:
- `/api/tools`
- `/api/heartbeat`
- `/api/memory`
- `/api/profile`

Communication evolution path:
1. REST + SSE (current)
2. optional SignalR for richer multi-client realtime UX
3. optional gRPC for strongly typed service-to-service streaming needs

---

## 12) Foundry Integration Guidance

- Use Microsoft Agent Framework / Foundry integration via `AIProjectClient`
- keep model/deployment configuration external and mutable by config
- keep SDK types behind adapters

---

## 13) Persistence and Data Strategy

- primary structured persistence baseline: SQLite + EF Core
- maintain provider abstractions for future expansion
- future expansion options:
  - vector store
  - semantic retrieval systems
  - external DB/search-backed memory

---

## 14) Security, Governance, and Observability

### Security/governance
- least-privilege file/tool access
- explicit policy checks for tool invocations
- approval requirements for configured high-risk actions
- immutable-enough audit records for sensitive paths
- secure secrets/config handling

### Observability minimum
- structured logs with correlation IDs
- traces across request -> bus -> tool -> response
- metrics for latency/queue depth/errors/retries/tool outcomes
- health checks for host dependencies and tool registry state

---

## 15) Recommended Libraries

### Host/API
- ASP.NET Core (Minimal APIs)
- Microsoft.Extensions.Hosting
- Microsoft.Extensions.Options
- Microsoft.Extensions.Http

### Foundry/Agent integration
- Azure.Identity
- Microsoft.Agents.AI.AzureAI
- Microsoft.Agents.AI.Workflows (when workflow orchestration is required)

### Persistence
- Microsoft.EntityFrameworkCore.Sqlite
- Microsoft.EntityFrameworkCore.Design

### Resilience
- Polly and/or built-in HttpClient resilience handlers

### Logging/telemetry
- Serilog.AspNetCore
- Serilog.Sinks.Console
- OpenTelemetry.Extensions.Hosting
- Azure.Monitor.OpenTelemetry.AspNetCore (optional)

### MCP
- ModelContextProtocol

### Validation/console/testing
- FluentValidation
- Spectre.Console
- xUnit
- FluentAssertions
- Microsoft.AspNetCore.Mvc.Testing

---

## 16) Prioritized Backlog (Epics)

Priority legend:
- **P0** foundational/blocking
- **P1** high-value core
- **P2** advanced/hardening

### Epic 1 (P0): Core Message Bus and Streaming Runtime
Goal: stable in-proc event loop and streaming flow with contract enforcement.

### Epic 2 (P0): Workspace Layout, Safety, and Persistence
Goal: durable workspace, scoped access, atomic writes, integrity checks.

### Epic 3 (P1): Session Engine and Lifecycle Management
Goal: multi-session isolation with replay/rejoin/retention/export controls.

### Epic 4 (P1): Short-Term + Long-Term Memory Pipeline
Goal: practical continuity with explainable retrieval and consolidation.

### Epic 5 (P1): Tool/Skill Registry, Discovery, and Permissions
Goal: unified discoverable policy-aware tool surface.

### Epic 6 (P1): Client Adapter Layer and Identity Boundaries
Goal: consistent behavior across client types with accurate attribution.

### Epic 7 (P2): Heartbeat and Background Automation
Goal: transparent periodic automation without UX degradation.

### Epic 8 (P2): Identity and Personalization Profiles
Goal: versioned AGENT/SOUL/USER personalization with safe boundaries.

### Epic 9 (P2): Observability, Reliability, and Governance Hardening
Goal: production-level diagnosability, resilience, and policy controls.

---

## 17) Delivery Sequence

1. **Phase A0**: Epic 2 workspace root/structure + DB relocation/migration
2. **Phase A1**: Epic 1 core bus + streaming runtime
3. **Phase B**: Epic 3 + Epic 5
4. **Phase C**: Epic 4 + Epic 6
5. **Phase D**: Epic 7 + Epic 8
6. **Phase E**: Epic 9

---

## 18) Definition of Done (Per Epic)

- design scope/interfaces/risks documented and approved
- implementation complete with tests
- telemetry added for success/failure paths
- user/developer docs updated
- migration/backward-compatibility approach documented when relevant

---

## 19) Consolidation Notes (What Changed from Legacy Docs)

This document supersedes the previously separate architecture files and intentionally merges strategy, implementation direction, and backlog framing into one canonical reference.

### Legacy files folded into this document
- `Raven.Architecture.Plan.md`
- `Raven.Architecture.Plan.Unified.md`
- `Raven.Architecture.Backlog.GitHub.md`
- `Raven.Core.DI.RegistrationMap.md`
- `Raven.Future.Suggestions.md`

### What was preserved
- Core architecture decisions and rationale (host model, transport, streaming-first design)
- Workspace durability/safety direction and path resolution rules
- Message bus/event model direction and metadata contracts
- Memory/tools/MCP/client/heartbeat/identity direction
- Delivery phases, epic-level backlog framing, and definition of done
- Current-state implementation snapshots that were previously in DI/architecture split docs

### Normalization performed
- Duplicative sections from old plan variants were reconciled into single sections
- Wording was aligned with current repository state (for example, .NET 10 baseline)
- Legacy "future suggestion" items were retained where still relevant and folded into directional sections

### Practical guidance for maintainers
- Treat this file as the source of truth for architecture/design direction
- Add new architecture decisions here instead of creating parallel plan docs
- If a future split becomes necessary, keep this as the index/root and reference specialized docs from here

---

This file is the canonical architecture/design reference under `docs/design`.
