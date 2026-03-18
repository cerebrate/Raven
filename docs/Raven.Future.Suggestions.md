# Future Suggestions

These are future suggestions for Raven. They are not currently on the Architecture Plan, but they are being considered for future development and should be kept in mind when implementing new features or making changes to the architecture.

## Bus

To smoothly handle multiple clients of different types, we should have an internal message bus with an event loop which handles messages received from clients and routes them appropriately, then returns the answer in like manner. This loop should be the core of the server (Raven.Core's) operation.

(Problem: can we keep the streaming response capabilities of the agent if we do this? Maybe we can have the bus handle streaming responses as well, or we can have the bus route messages to a separate streaming handler that can handle streaming responses.)

Clients in this case should include both human users and internal systems such as schedulers, subagents, some tools, etc. This would allow us to have a more modular and scalable architecture, as we can easily add new clients and handlers without having to modify the core agent logic. It would also allow us to have better separation of concerns, as the agent can focus on its core logic while the bus handles the routing of messages.

## Workspace

The ability to have a persistent workspace that the agent can read from and write to would be incredibly useful. This would allow the agent to store information, files, and other data that it can access later. This could be implemented as a simple file system that the agent can interact with, or it could be a more complex database system.

## Memory

Raven should have both short-term and long-term memory capabilities. Short-term memory should be something simple and quick (like a basic MEMORY.md file that the agent can read from and write to) suitable for use as a scratchpad, while long-term memory could be more complex and structured (like a vector database or knowledge graph). The agent should be able to use its memory to store information about the world, its goals, its past actions, and any other relevant data that it can use to make informed decisions.

Short-term memory should be consolidated at the end of each session and relevant information should be extracted and stored in long-term memory. This way, the agent can have a more structured and organized memory system that it can use to make better decisions in the future.

There should probably be a history log of actions taken as part of the memory system. Ideally, this should be structured in a human-readable way, but rather than bogging down the agent with parsing this directly, relevant information from it should be extracted and stored in the more structured long-term memory for the agent to use. This way, we can have the best of both worlds: a human-readable history log for transparency and debugging, and a structured memory system for the agent's use.

## Tools and Skills

We will both be accepting these from MCPs and hosting some internally. These should, for the most part, be presented identically to the agent.

Rather than burn context by listing every skill and tool to the agent in its prompt, we should present the agent with a tool/skill register/wrapper which it can use to find applicable tools and skills on the fly.

Only the most fundamental tools and skills should go in the prompt. The rest should be discoverable by the agent as needed. Fundamentals would include:

- Tool/skill searching/execution via the register/wrapper
- cron, for scheduling reminders and tasks
- MCP client (which operates via the register/wrapper)
- The ability to execute shell commands _within the agent's workspace_.
- The ability to spawn background subagents to perform tasks asynchronously/in the background.
- Basic web search capabilities, such as searching for information on the web, retrieving data from APIs, etc.

We'll need an interface with the client for tools to ask permissions to the user before executing certain actions, such as executing a shell command, accessing certain files, or using certain skills. This interface should be flexible enough to allow for different types of permissions and different ways of asking for them (e.g., a simple yes/no prompt, a more detailed explanation of the action being requested, etc.). Such permissions should also be configurable on top of tools and skills, so that users can set default permissions for certain actions if they choose to.

## Clients

Future clients could include:

- a web interface/dashboard for users to interact with the agent, view its memory, manage tools and skills, etc. The chat interface will be part of this dashboard, but we can also have other interfaces for different purposes (e.g., a more visual interface for managing tools and skills, a dashboard for monitoring the agent's activity and performance, etc.)
- a chat-channel interface for users to interact with the agent in a more conversational way.

These clients should be designed to be modular and extensible, and implemented as secondary projects which interact with the core Raven server via a well-defined API. This way, we can easily add new clients in the future without having to modify the core server logic, and we can also allow third-party developers to create their own clients if they choose to.

(For the latter, we may wish to make a distinction between conversations _with_ the agent, which it should take as instructions, and conversations merely _in its presence_, which it should be able to observe and learn from but not necessarily take as instructions. This would allow for more flexible interactions with the agent, as users could have conversations in the same channel as the agent without necessarily expecting it to act on every message.

We may also need to handle the scenario that CLI or Web clients will involve one user talking with the agent, whereas chat-channel conversations may involve multiple users. Correctly identifying the user in these cases will be important for long-term memory and implicit requests to access information.)

## Heartbeat

In addition to the cron tool for scheduled tasks, we should implement a heartbeat mechanism that allows the agent to perform regular checks on its own status, its environment, and any ongoing tasks. This could be implemented as a simple timer that triggers a heartbeat function via the message bus at regular intervals (e.g., every minute).

Heartbeat tasks, etc., should be stored in a dedicated portion of the workspace, and the agent should be able to read from and write to this portion of the workspace as needed. This would allow the agent to keep track of its ongoing tasks, perform regular maintenance, and ensure that it is functioning properly over time. They should also be user-transparent, such that users can easily see what heartbeat tasks are scheduled and what they do, and they should be configurable so that users can add, remove, or modify heartbeat tasks as needed. The agent should surface to the user any time it adds or reconfigures a heartbeat task, so that users are always aware of what the agent is doing in the background.

## Session Management

We should be able to manage multiple sessions with the agent, each with its own context and memory. This would allow users to have different conversations or interactions with the agent without them interfering with each other. Each session should be able to access the shared long-term memory, but they should have their own short-term memory and context.

Sessions should be recorded and stored until explicitly deleted by the user (which should include deleting all sessions older than X), and users should be able to review past sessions, extract information from them, or even rejoin them for further conversation as needed.

## Identity and Personalization

The identity of the agent should be customizable and personalizable by the user. This could include things like the agent's name, its personality traits, its preferences, etc. This would allow users to create a more personalized and engaging experience with the agent, and it would also allow the agent to better understand and cater to the user's needs and preferences over time.

We should use a multiple-section system for this, similar to Nanobot and OpenClaw, where an AGENT section contains more technical information, a SOUL section contains information pertaining to the agent's personality, values, and communication style, and a USER section contains information about the user that the agent can use to personalize its interactions. This way, we can have a more structured and organized system for managing the agent's identity and personalization, and we can also allow users to easily customize and update this information as needed.

## Suggested Improvements and Implementation Notes

### Bus

- Define envelope contracts early (`MessageId`, `CorrelationId`, `CausationId`, `SessionId`, `UserId`, `Type`, `Priority`, timestamps).
- Keep streaming as first-class by modeling chunk events (e.g., `ResponseStarted`, `ResponseDelta`, `ResponseCompleted`, `ResponseFailed`).
- Add backpressure controls and bounded queues to prevent unbounded memory growth.
- Make handlers idempotent where possible; this simplifies retries and crash recovery.
- Consider an in-proc bus first, with a transport abstraction to support future distributed mode.

### Workspace

- Partition workspace by concern (`/sessions`, `/memory`, `/heartbeat`, `/artifacts`, `/audit`).
- Add capability-scoped file access policies so tools only access allowed paths.
- Use atomic write patterns for critical files (`*.tmp` + replace) to reduce corruption risk.
- Add retention and garbage-collection policies for artifacts and transient outputs.

### Memory

- Introduce memory tiers: scratchpad (volatile), episodic (session summaries), semantic (facts/preferences).
- Store provenance for each memory item (source session, timestamp, confidence, last-validated).
- Add conflict resolution rules for contradictory memories.
- Run background compaction: summarize, deduplicate, re-score relevance.
- Expose memory explainability in UX: why a memory was retrieved and when it was learned.

### Tools and Skills

- Create a unified manifest schema for internal and MCP-hosted tools.
- Include discoverability metadata: tags, cost, latency profile, risk level, required permissions.
- Add tool health checks and circuit breakers to avoid repeated failing calls.
- Add explicit side-effect declarations (read-only vs mutating) to improve planning safety.
- Include deterministic replay metadata for audit/debug (`inputs`, `selected tool`, `result hash`).

### Clients

- Treat clients as adapters over a shared session and event API contract.
- Define identity boundary rules up front (single-user local vs multi-user channels).
- For channel clients, support scoped visibility and mention-directed intent routing.
- Add capability negotiation so clients can advertise support for streaming, attachments, approvals, etc.

### Heartbeat

- Use separate heartbeat classes: health probes, maintenance jobs, delayed reminders.
- Add run budget and timeout per task to prevent starvation of interactive requests.
- Persist next-run and last-run state to survive restarts.
- Surface all heartbeat-created/modified automations in a human-readable activity log.

### Session Management

- Use append-only event logs per session; derive current state through snapshots.
- Support branch/fork of sessions for what-if exploration without contaminating the original.
- Add export/import format for portability and external analysis.
- Define deletion semantics clearly: hard delete, soft delete, and legal/compliance retention exceptions.

### Identity and Personalization

- Version the AGENT/SOUL/USER documents and track diffs over time.
- Separate runtime-safe persona knobs from restricted system behavior policies.
- Add trust levels for USER facts (explicit user-stated vs inferred).
- Provide a preview/simulation mode so users can inspect personality changes before applying.

### Cross-Cutting Recommendations

- **Observability:** structured logs, traces, per-tool latency/error metrics, correlation IDs everywhere.
- **Security:** least-privilege permissions, secret isolation, immutable audit trail for sensitive actions.
- **Reliability:** retries with jitter, idempotency keys, dead-letter queue semantics for failed workflows.
- **Governance:** policy engine for action gating (tool allowlists, path restrictions, high-risk confirmation).
- **Roadmap approach:** build vertical slices (single client + bus + memory + tools) before broad feature expansion.

### Suggested Incremental Milestones

1. **M1 - Core runtime skeleton:** in-proc bus, session envelope, streaming event model, structured logging.
2. **M2 - Workspace + short-term memory:** scoped file system layout, scratchpad, session summaries.
3. **M3 - Tool registry and approvals:** discoverable tool manifests, permission prompts, audit records.
4. **M4 - Multi-client support:** CLI + web adapter parity, identity/session boundary handling.
5. **M5 - Long-term memory + heartbeat:** semantic memory store, compaction, scheduled background tasks.
6. **M6 - Hardening:** policy controls, failure injection tests, observability dashboards, retention controls.

## Prioritized Architecture Backlog (Epics, Tasks, Acceptance Criteria)

This backlog is designed to turn the ideas above into executable work with clear delivery checkpoints.

### Priority Legend

- **P0:** Foundation; blocks multiple downstream areas.
- **P1:** High value; can proceed once P0 is in place.
- **P2:** Optimization/hardening and advanced capabilities.

---

## Epic 1 (P0): Core Message Bus and Streaming Runtime

**Goal:** Establish a stable in-process event loop and bus that supports both normal and streaming responses.

### Tasks

1. Define canonical message envelope and event type taxonomy.
2. Implement in-proc dispatcher with bounded queues and backpressure.
3. Implement streaming event path (`Started/Delta/Completed/Failed`).
4. Add correlation propagation (`MessageId`, `CorrelationId`, `SessionId`, `UserId`).
5. Add dead-letter handling for unrecoverable handler failures.

### Acceptance Criteria

- Any request is represented as a typed envelope with required metadata.
- Streaming outputs are delivered incrementally without blocking the event loop.
- Queue pressure is observable and bounded (no unbounded growth under load test).
- Failed messages are captured with reason and retry metadata.
- End-to-end traces can correlate request -> tool calls -> response events.

---

## Epic 2 (P0): Workspace Layout, Safety, and Persistence

**Goal:** Introduce a clear, durable workspace model with scoped access and safe writes.

### Tasks

1. Create workspace directory contract (`sessions/`, `memory/`, `heartbeat/`, `artifacts/`, `audit/`).
2. Implement path-policy guardrails for tool/file access.
3. Add atomic write utility for critical persisted files.
4. Implement retention rules for transient artifacts.
5. Add startup integrity check and recovery for partially written files.

### Acceptance Criteria

- Workspace is automatically initialized with expected structure.
- Tool actions cannot access blocked paths outside allowed scopes.
- Critical writes are atomic and recoverable after simulated crash.
- Retention job removes expired artifacts according to policy.
- Integrity scan reports status and actionable remediation.

---

## Epic 3 (P1): Session Engine and Lifecycle Management

**Goal:** Support multiple isolated sessions with replay, rejoin, and retention controls.

### Tasks

1. Implement per-session append-only event log.
2. Add session snapshots for fast resume.
3. Implement session list/open/rejoin/delete APIs.
4. Add retention controls (delete older than X, soft/hard delete policy).
5. Add export/import format for session portability.

### Acceptance Criteria

- Multiple sessions run concurrently without context leakage.
- Rejoin restores state equivalent to pre-shutdown behavior.
- Delete policies are enforced and auditable.
- Exported session can be imported and replayed consistently.
- Session metadata includes ownership and timestamps.

---

## Epic 4 (P1): Short-Term + Long-Term Memory Pipeline

**Goal:** Deliver practical memory that improves continuity while remaining explainable.

### Tasks

1. Implement short-term scratchpad storage per session.
2. Implement episodic consolidation at session end.
3. Implement semantic memory schema with provenance and confidence.
4. Add retrieval ranking with recency + relevance scoring.
5. Add memory conflict resolution and deduplication routines.

### Acceptance Criteria

- Session scratchpad is available during active conversation.
- Session close triggers deterministic consolidation pipeline.
- Long-term memory entries contain source, confidence, and timestamps.
- Retrieval endpoint can explain “why this memory was selected.”
- Contradictory items are flagged/resolved per configured policy.

---

## Epic 5 (P1): Tool/Skill Registry, Discovery, and Permissions

**Goal:** Unify internal and external tools behind one discoverable, policy-aware interface.

### Tasks

1. Define tool manifest schema (capabilities, risks, side effects, permissions).
2. Build registry API for search/filter/select.
3. Add approval flow for high-risk tool actions.
4. Add tool health checks and temporary disable/circuit-break states.
5. Add audit entries for tool invocation intent, input summary, and outcome.

### Acceptance Criteria

- Agent can discover tools at runtime via registry query.
- Mutating/high-risk tools trigger permission workflow when required.
- Unhealthy tools are skipped or downgraded according to policy.
- Audit trail captures who/what/when for each invocation.
- Internal and MCP tools are presented through the same interface shape.

---

## Epic 6 (P1): Client Adapter Layer and Identity Boundaries

**Goal:** Enable consistent behavior across CLI, web, and channel clients with correct user attribution.

### Tasks

1. Define client adapter contract (events, messages, attachments, approvals, streaming).
2. Implement identity mapping strategy per client type.
3. Add conversation intent classification (`to-agent` vs `in-presence`).
4. Implement mention-based routing for channel clients.
5. Add capability negotiation (streaming, approvals, attachments).

### Acceptance Criteria

- CLI/web/channel clients can attach through a common adapter API.
- User identity is accurately captured in all session events.
- Non-addressed channel chatter does not trigger unintended actions.
- Client capabilities are discovered at connect time and respected.
- Streaming behavior is consistent across all supported clients.

---

## Epic 7 (P2): Heartbeat and Background Automation

**Goal:** Provide transparent periodic tasks without harming interactive responsiveness.

### Tasks

1. Implement heartbeat scheduler integrated with bus.
2. Add task categories (health, maintenance, reminders).
3. Persist task state (`nextRun`, `lastRun`, `lastStatus`).
4. Add runtime budgets/timeouts per task.
5. Expose user-facing view for scheduled heartbeat actions.

### Acceptance Criteria

- Heartbeat tasks survive restart and continue with correct schedule.
- Long-running heartbeat tasks do not starve foreground requests.
- Users can inspect, enable/disable, and edit heartbeat tasks.
- Agent emits visible notice when adding/changing heartbeat automations.
- Task failures are retried or reported per policy.

---

## Epic 8 (P2): Identity and Personalization Profiles

**Goal:** Support customizable AGENT/SOUL/USER configuration with clear boundaries and versioning.

### Tasks

1. Define schema and storage format for AGENT/SOUL/USER sections.
2. Add profile versioning and diff history.
3. Add trust levels for user attributes (explicit vs inferred).
4. Add preview mode for personality/profile changes.
5. Add policy layer to prevent unsafe profile overrides.

### Acceptance Criteria

- Profile is editable and versioned with rollback support.
- Inferred user facts are distinguishable from explicit user-provided facts.
- Preview mode shows expected behavior impact before apply.
- Restricted safety/system policies cannot be overridden by persona settings.
- Profile updates are auditable and attributable.

---

## Epic 9 (P2): Observability, Reliability, and Governance Hardening

**Goal:** Make the platform diagnosable, resilient, and policy-controlled for production operation.

### Tasks

1. Add structured logs and distributed trace hooks across core flows.
2. Add metrics for bus depth, latency, failures, and tool outcomes.
3. Implement retry policies with jitter and idempotency keys.
4. Add governance policy engine (tool allowlist, path constraints, risk confirmations).
5. Add failure injection and recovery test suite.

### Acceptance Criteria

- Core workflows emit logs/traces with correlation IDs.
- Dashboard/queries can reveal top failure causes and latency hotspots.
- Retry behavior is deterministic and bounded per policy.
- Governance policies can block or require approval for configured actions.
- Failure-injection tests pass defined recovery SLOs.

---

## Suggested Delivery Sequence

1. **Phase A (P0):** Epic 1 + Epic 2
2. **Phase B (P1 core):** Epic 3 + Epic 5
3. **Phase C (P1 intelligence):** Epic 4 + Epic 6
4. **Phase D (P2 automation/persona):** Epic 7 + Epic 8
5. **Phase E (P2 hardening):** Epic 9

## Suggested Definition of Done (for each Epic)

- Design doc merged (scope, interfaces, risks, rollout).
- Core implementation complete with integration tests.
- Telemetry added for success/failure paths.
- User-facing behavior documented.
- Backward compatibility and migration plan documented (if applicable).

