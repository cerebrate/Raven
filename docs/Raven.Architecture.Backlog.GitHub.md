# Raven Architecture Backlog (GitHub Issues Draft)

This file provides copy/paste-ready issue drafts based on:
- `docs/Raven.Architecture.Plan.Unified.md`

Recommended labels:
- `area:architecture`
- `area:core` / `area:memory` / `area:tools` / `area:clients` / `area:ops`
- `priority:P0` / `priority:P1` / `priority:P2`
- `type:epic`

---

## Epic 1 (P0): Core Message Bus and Streaming Runtime

**Title**
`[Epic][P0] Core Message Bus and Streaming Runtime`

**Labels**
`type:epic`, `priority:P0`, `area:core`, `area:architecture`

**Body**
### Goal
Establish a stable in-process event loop and message bus that supports both standard and streaming responses.

### Scope Checklist
- [ ] Define canonical message envelope and event taxonomy.
- [ ] Implement in-proc dispatcher with bounded queues and backpressure.
- [ ] Implement streaming event path (`ResponseStarted`, `ResponseDelta`, `ResponseCompleted`, `ResponseFailed`).
- [ ] Propagate correlation/session/user metadata (`MessageId`, `CorrelationId`, `SessionId`, `UserId`).
- [ ] Add dead-letter handling for unrecoverable failures.

### Acceptance Criteria
- [ ] All runtime requests are represented as typed envelopes with required metadata.
- [ ] Streaming outputs are delivered incrementally without blocking main event loop.
- [ ] Queue growth is bounded and measurable under load.
- [ ] Failed messages are captured with reason and retry metadata.
- [ ] End-to-end trace correlation exists for request -> tool -> response.

### Dependencies
- None (foundational).

### Notes
- Start in-proc; keep transport abstraction for future distributed mode.

---

## Epic 2 (P0): Workspace Layout, Safety, and Persistence

**Title**
`[Epic][P0] Workspace Layout, Safety, and Persistence`

**Labels**
`type:epic`, `priority:P0`, `area:core`, `area:architecture`

**Body**
### Goal
Create a durable workspace with clear partitioning, safe writes, and scoped file access.

### Scope Checklist
- [ ] Implement workspace directory contract (`sessions/`, `memory/`, `heartbeat/`, `artifacts/`, `audit/`).
- [ ] Add capability-scoped path access policies.
- [ ] Add atomic write utility for critical files.
- [ ] Implement retention and garbage collection for transient artifacts.
- [ ] Add startup integrity checks and partial-write recovery.

### Acceptance Criteria
- [ ] Workspace auto-initializes with expected structure.
- [ ] Unauthorized file paths are blocked by policy.
- [ ] Critical writes are crash-safe.
- [ ] Retention policies execute correctly.
- [ ] Integrity scan reports actionable issues.

### Dependencies
- None (foundational; should run alongside Epic 1).

---

## Epic 3 (P1): Session Engine and Lifecycle Management

**Title**
`[Epic][P1] Session Engine and Lifecycle Management`

**Labels**
`type:epic`, `priority:P1`, `area:core`, `area:architecture`

**Body**
### Goal
Support isolated multi-session behavior with replay, rejoin, retention, and portability.

### Scope Checklist
- [ ] Implement append-only event log per session.
- [ ] Add session snapshots for fast resume.
- [ ] Implement session list/open/rejoin/delete operations.
- [ ] Add retention support (`delete older than X`, soft/hard delete policy).
- [ ] Add export/import support.

### Acceptance Criteria
- [ ] Sessions can run concurrently without context leakage.
- [ ] Rejoin restores expected session state.
- [ ] Deletion semantics are enforceable and auditable.
- [ ] Export/import preserves replay consistency.

### Dependencies
- Epic 1, Epic 2.

---

## Epic 4 (P1): Short-Term + Long-Term Memory Pipeline

**Title**
`[Epic][P1] Short-Term + Long-Term Memory Pipeline`

**Labels**
`type:epic`, `priority:P1`, `area:memory`, `area:architecture`

**Body**
### Goal
Provide practical memory continuity with explainable retrieval.

### Scope Checklist
- [ ] Implement short-term scratchpad memory per session.
- [ ] Implement end-of-session episodic consolidation.
- [ ] Implement semantic memory schema (provenance, confidence, timestamps).
- [ ] Add retrieval ranking (relevance + recency).
- [ ] Add conflict resolution and deduplication routines.

### Acceptance Criteria
- [ ] Scratchpad is available during active sessions.
- [ ] Session closure triggers deterministic consolidation.
- [ ] Long-term entries include provenance/confidence metadata.
- [ ] Retrieval can explain why memory was selected.
- [ ] Contradictory memory is flagged and handled by policy.

### Dependencies
- Epic 2, Epic 3.

---

## Epic 5 (P1): Tool/Skill Registry, Discovery, and Permissions

**Title**
`[Epic][P1] Tool/Skill Registry, Discovery, and Permissions`

**Labels**
`type:epic`, `priority:P1`, `area:tools`, `area:architecture`

**Body**
### Goal
Unify internal and MCP tools behind a discoverable, policy-aware registry.

### Scope Checklist
- [ ] Define unified manifest schema (capabilities, risk, side effects, permissions).
- [ ] Implement registry query/search/filter API.
- [ ] Implement approval workflow for high-risk actions.
- [ ] Add tool health checks and circuit-break states.
- [ ] Add audit records for intent/input summary/outcome.

### Acceptance Criteria
- [ ] Runtime tool discovery works across internal and MCP tools.
- [ ] High-risk tool usage requires approval when policy says so.
- [ ] Unhealthy tools are bypassed/disabled safely.
- [ ] Tool calls are audit-traceable.

### Dependencies
- Epic 1, Epic 2.

---

## Epic 6 (P1): Client Adapter Layer and Identity Boundaries

**Title**
`[Epic][P1] Client Adapter Layer and Identity Boundaries`

**Labels**
`type:epic`, `priority:P1`, `area:clients`, `area:architecture`

**Body**
### Goal
Enable consistent behavior across CLI/web/channel clients with correct user attribution.

### Scope Checklist
- [ ] Define client adapter contract (events, streaming, attachments, approvals).
- [ ] Implement identity mapping by client type.
- [ ] Add intent classification (`to-agent` vs `in-presence`).
- [ ] Add mention-based routing for channel clients.
- [ ] Add capability negotiation for optional features.

### Acceptance Criteria
- [ ] Common adapter contract works across target client types.
- [ ] User identity is correctly persisted with session events.
- [ ] Non-addressed messages do not trigger unintended execution.
- [ ] Streaming and approvals respect client capability negotiation.

### Dependencies
- Epic 1, Epic 3, Epic 5.

---

## Epic 7 (P2): Heartbeat and Background Automation

**Title**
`[Epic][P2] Heartbeat and Background Automation`

**Labels**
`type:epic`, `priority:P2`, `area:ops`, `area:architecture`

**Body**
### Goal
Add transparent background automation without impacting interactive performance.

### Scope Checklist
- [ ] Implement heartbeat scheduler integrated with the message bus.
- [ ] Define task classes (health, maintenance, reminders).
- [ ] Persist task state (`nextRun`, `lastRun`, `lastStatus`).
- [ ] Enforce per-task run budget and timeout.
- [ ] Provide user-visible listing/management for heartbeat tasks.

### Acceptance Criteria
- [ ] Heartbeat schedules survive restarts.
- [ ] Heartbeat work does not starve foreground request handling.
- [ ] Users can inspect and modify heartbeat tasks.
- [ ] Agent surfaces all heartbeat-created/updated automations.

### Dependencies
- Epic 1, Epic 2, Epic 5.

---

## Epic 8 (P2): Identity and Personalization Profiles

**Title**
`[Epic][P2] Identity and Personalization Profiles`

**Labels**
`type:epic`, `priority:P2`, `area:core`, `area:architecture`

**Body**
### Goal
Support versioned AGENT/SOUL/USER profiles with safe policy boundaries.

### Scope Checklist
- [ ] Define profile schema/storage for AGENT/SOUL/USER.
- [ ] Add versioning and diff/rollback support.
- [ ] Add trust levels for USER facts (explicit vs inferred).
- [ ] Add preview mode for profile changes.
- [ ] Prevent unsafe override of restricted system/safety policies.

### Acceptance Criteria
- [ ] Profile updates are versioned and auditable.
- [ ] Explicit and inferred USER facts are distinguishable.
- [ ] Preview mode demonstrates expected behavior changes.
- [ ] Safety-critical controls remain non-overridable.

### Dependencies
- Epic 3, Epic 4.

---

## Epic 9 (P2): Observability, Reliability, and Governance Hardening

**Title**
`[Epic][P2] Observability, Reliability, and Governance Hardening`

**Labels**
`type:epic`, `priority:P2`, `area:ops`, `area:architecture`

**Body**
### Goal
Make the platform diagnosable, resilient, and policy-controlled for production-style operation.

### Scope Checklist
- [ ] Add structured logs and traces across core paths.
- [ ] Add metrics for queue depth, latency, failures, retries, and tool outcomes.
- [ ] Implement retry policies with jitter and idempotency keys.
- [ ] Implement governance policy engine (allowlist/path/risk rules).
- [ ] Add failure injection and recovery validation tests.

### Acceptance Criteria
- [ ] Correlation IDs span core end-to-end paths.
- [ ] Dashboards/queries reveal latency and failure hotspots.
- [ ] Retry behavior is bounded and deterministic per policy.
- [ ] High-risk operations are policy-gated.
- [ ] Recovery behavior is validated with automated failure tests.

### Dependencies
- Epic 1 through Epic 8.

---

## Suggested Milestone Mapping

- **Milestone: Phase A (P0 Foundation)**
  - Epic 1, Epic 2
- **Milestone: Phase B (P1 Core Runtime)**
  - Epic 3, Epic 5
- **Milestone: Phase C (P1 Intelligence + Clients)**
  - Epic 4, Epic 6
- **Milestone: Phase D (P2 Features)**
  - Epic 7, Epic 8
- **Milestone: Phase E (P2 Hardening)**
  - Epic 9

---

## Optional Child Issue Template (for each Epic task)

**Title**
`[Task] <short task title>`

**Body**
### Parent
- Epic: #<epic-issue-number>

### Outcome
- <what this task delivers>

### Checklist
- [ ] Implementation complete
- [ ] Tests added/updated
- [ ] Telemetry added/updated
- [ ] Docs updated

### Done Criteria
- <task-specific completion conditions>
