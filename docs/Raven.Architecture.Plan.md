# Raven Solution Architecture Plan

## Status
Design only. No implementation is included in this plan.

## Goals
- Build **Raven.Core** as a local .NET host for an AI agent backed by a model deployed in **Microsoft Foundry**.
- Build **Raven.Client.Console** as a simple console chat client that communicates with Raven.Core.
- Keep the first iteration small, but structure Raven.Core so that memory, skills, MCP integration, and periodic jobs can grow without large rewrites.

---

## Recommended Solution Shape

### Minimum projects for the first pass
1. **Raven.Core**
   - ASP.NET Core host for agent runtime, client communication, MCP coordination, memory orchestration, and scheduled work.
2. **Raven.Client.Console**
   - Console chat client with a REPL-style experience that talks to Raven.Core over HTTP.

### Strongly recommended follow-on projects
These do not need to be created yet, but Raven.Core should be designed so they can be extracted cleanly later.

3. **Raven.Contracts**
   - Shared DTOs and protocol contracts used by Raven.Core and Raven.Client.Console.
4. **Raven.Core.Tests**
   - Unit and integration tests for the host.
5. **Raven.Client.Console.Tests**
   - Tests for console command parsing, rendering, and API client behavior.

---

## Primary Architectural Recommendation

Use **Raven.Core** as an **ASP.NET Core application with the Generic Host**, not as a plain console worker.

### Why
- It can expose HTTP endpoints for multiple client types.
- It can host background services for periodic jobs.
- It gets dependency injection, configuration, logging, health checks, and hosted service support by default.
- It gives a clean path from a local-only host to a future desktop, mobile, web, or service-hosted environment.

### Initial communication choice
Use **HTTP + JSON** for requests and **Server-Sent Events (SSE)** or streaming HTTP responses for token/message streaming.

### Why this is the best first choice
- Simpler than SignalR or gRPC for the first client.
- Easy for a console client to consume.
- Good fit for chat request/response and streaming assistant output.
- Leaves room to add SignalR later if real-time multi-client features become important.

---

## Raven.Core – Target Responsibilities

Raven.Core should own the following responsibilities:

1. **Client communication**
   - Accept chat requests.
   - Stream agent responses.
   - Expose session and conversation operations.

2. **Agent runtime orchestration**
   - Resolve the configured model deployment in Microsoft Foundry.
   - Create or reuse an agent/conversation per session.
   - Manage prompt assembly, tool access, memory retrieval, and response streaming.

3. **Memory orchestration**
   - Store conversation/session metadata.
   - Maintain app-owned memory beyond Foundry conversation state.
   - Support later addition of semantic memory or long-term memory.

4. **Skill and tool hosting**
   - Register local plug-in skills.
   - Mediate which tools the agent can use.
   - Provide a clean approval and auditing path for sensitive tools.

5. **MCP integration**
   - Connect to one or more MCP servers.
   - Route tool access through a controlled gateway.
   - Support approval, authentication, and observability around MCP calls.

6. **Periodic jobs**
   - Run scheduled summarization, cleanup, retention, cache warmup, and indexing tasks.

7. **Operational concerns**
   - Logging, tracing, metrics, configuration, secrets, and health checks.

---

## Raven.Core – Internal Architecture

Even if Raven.Core starts as a single project, it should be split internally into clear layers/folders.

### Recommended internal slices
- **Host**
  - Program startup
  - DI registration
  - configuration
  - logging
  - HTTP endpoints
- **Api**
  - chat endpoints
  - session endpoints
  - health endpoints
- **Application**
  - use-case orchestration
  - command/query handlers or application services
- **AgentRuntime**
  - Foundry client adapter
  - conversation orchestration
  - prompt composition
  - tool execution flow
- **Memory**
  - conversation metadata store
  - summaries
  - user profile memory
  - future semantic memory provider abstraction
- **Skills**
  - local tools exposed to the agent
  - skill registry
  - tool policies and approval rules
- **Mcp**
  - MCP client/gateway
  - server registrations
  - auth and approval
- **Jobs**
  - periodic background services
- **Infrastructure**
  - persistence
  - external service adapters
  - filesystem access
  - time/clock abstractions
- **Contracts**
  - DTOs used by the API until a dedicated Raven.Contracts project exists

---

## Raven.Core – Suggested High-Level Components

### 1. Chat API surface
Initial endpoints should stay small.

Recommended first endpoints:
- `POST /api/chat/sessions`
  - Create a new client-visible chat session.
- `POST /api/chat/sessions/{sessionId}/messages`
  - Send a user message and return an agent response.
- `POST /api/chat/sessions/{sessionId}/messages/stream`
  - Stream the response incrementally.
- `GET /api/chat/sessions/{sessionId}`
  - Return conversation metadata and recent history.
- `DELETE /api/chat/sessions/{sessionId}`
  - End the session and apply retention/cleanup policy.

### 2. Session coordinator
Responsible for:
- mapping client session IDs to Foundry conversation IDs
- loading conversation metadata
- applying retention rules
- deciding when to summarize or compact history

### 3. Agent orchestrator
Responsible for:
- assembling system instructions
- injecting memory context
- determining which skills/tools are enabled
- invoking the Foundry agent/conversation client
- handling streaming and tool approval loops

### 4. Memory service
Separate **agent conversation state** from **application-owned memory**.

Recommended memory categories:
- **Conversation state**
  - session ID
  - Foundry conversation ID
  - timestamps
  - status
- **Conversation history cache**
  - optional local cache of selected exchanges
- **Conversation summaries**
  - rolling summaries for long chats
- **User profile memory**
  - durable user preferences and known facts
- **Operational memory**
  - pending tasks, job state, MCP auth metadata

### 5. Skill registry
A registry of locally hosted skills that can be exposed to the agent.

Examples:
- filesystem tools
- local diagnostics tools
- calendar/task tools
- domain-specific business tools
- wrappers over MCP tools when the app wants a local policy boundary

### 6. MCP gateway
Responsible for:
- registering remote MCP servers
- authenticating to them
- filtering allowed tools
- handling approval decisions
- logging and tracing tool calls

### 7. Background job runner
Use hosted services for the first version.

Likely initial jobs:
- conversation summarization
- stale session cleanup
- memory compaction
- MCP capability refresh
- local cache cleanup

---

## Raven.Core – Microsoft Foundry Design Guidance

### Recommended agent approach
Use **Microsoft Agent Framework** with **Microsoft Foundry agents backed by `AIProjectClient`**.

### Why
- It aligns with current Microsoft guidance for Foundry-based agent applications.
- It supports persisted conversations/conversation items.
- It provides a better path for tool use, workflows, and agent evolution than building everything directly against low-level chat calls.

### Key design rule
Do **not** let Foundry SDK types leak everywhere through the codebase.

Instead, isolate them behind interfaces such as:
- `IAgentConversationService`
- `IAgentResponseStreamer`
- `IAgentToolCoordinator`
- `IAgentMemoryAssembler`

That keeps the host portable if the implementation later changes.

### Model recommendation
Make the model deployment **configuration-driven**.

Initial default recommendation:
- **`gpt-5-mini`** for interactive chat, tool use, and lower latency/cost during early development.

Keep the following configurable:
- Foundry project endpoint
- agent name
- deployment name
- conversation retention policy
- enabled tools/MCP servers

---

## Raven.Core – Memory Strategy

### Important recommendation
Do not treat Foundry conversation persistence as the whole memory story.

Foundry conversation state is useful, but Raven.Core should also own app-level memory.

### First-phase memory design
Use a provider model:
- `IMemoryStore`
- `IConversationStore`
- `IConversationSummaryStore`
- `IUserProfileStore`

### Recommended first implementation
Start with a **simple local durable store** for app-owned state.

Good options:
- **SQLite + EF Core** for structured local persistence
- or **LiteDB** if the team wants a simpler single-file local store

### Recommendation
Prefer **SQLite + EF Core** because:
- it is well understood
- easy to inspect locally
- easy to migrate later
- fits conversation/session metadata well

### Future memory expansion
Design so these can be added later without changing API contracts:
- semantic retrieval store
- vector store
- Azure AI Search-backed knowledge memory
- external database-backed memory

---

## Raven.Core – MCP Strategy

### Recommended approach
Support **two MCP modes** conceptually:

1. **Host-managed MCP access**
   - Raven.Core connects to MCP servers itself.
   - Best when the host wants strict approval, auditing, or custom tool wrapping.

2. **Foundry-managed MCP tool access**
   - Foundry agent uses configured MCP tools.
   - Best when leaning into Foundry-native tool orchestration.

### Architecture recommendation
Design an abstraction that allows both, even if only one is implemented first.

Suggested abstractions:
- `IMcpGateway`
- `IMcpServerRegistry`
- `IMcpApprovalService`
- `IMcpToolPolicy`

### Security recommendation
All MCP tools should pass through explicit policy checks for:
- allow/deny
- user confirmation requirements
- server-level authentication
- audit logging

### SDK recommendation
For host-side MCP work, prefer the official **MCP C# SDK**:
- **`ModelContextProtocol`** package (preview at the time of writing)

---

## Raven.Core – Jobs and Scheduling

### First implementation recommendation
Use **`BackgroundService` / `IHostedService`**.

### Why
- Native to the .NET host
- Sufficient for periodic jobs
- Lower complexity than adding a scheduler immediately

### When to add a scheduler later
If jobs require:
- calendars/cron expressions
- persistence across restarts
- retry dashboards
- multiple independent schedules

then consider:
- **Quartz.NET**

### Initial recommendation
Do **not** start with Quartz unless requirements become more complex.

---

## Raven.Core – Communication Protocol Recommendation

### First transport
- **REST + streaming HTTP/SSE**

### Why not gRPC first
- Great technically, but more setup than needed for a simple console client.

### Why not SignalR first
- Best for multi-client push and bidirectional live UI features.
- Likely unnecessary for an initial console client.

### Evolution path
1. Start with REST + SSE
2. Add SignalR later for richer clients
3. Add gRPC later if strongly typed service-to-service streaming becomes important

---

## Raven.Client.Console – Architecture

### Purpose
A thin interactive client for manual chat testing and early usage.

### Responsibilities
- create/open a session
- send user input to Raven.Core
- display streamed responses
- support basic commands such as:
  - `/new`
  - `/exit`
  - `/history`
  - `/help`

### Recommended internal pieces
- **ConsoleLoop**
  - REPL loop
- **RavenApiClient**
  - wraps HTTP calls to Raven.Core
- **ConsoleRenderer**
  - output formatting and streaming rendering
- **SessionState**
  - current session ID and local metadata

### Recommendation
Keep this client deliberately thin. It should contain no business logic that belongs in Raven.Core.

---

## Recommended Libraries

### Core host and API
- **ASP.NET Core / Minimal APIs**
- **Microsoft.Extensions.Hosting**
- **Microsoft.Extensions.Options**
- **Microsoft.Extensions.Http**

### Microsoft Foundry / agent integration
- **Azure.Identity**
- **Microsoft.Agents.AI.AzureAI** `--prerelease`
- **Microsoft.Agents.AI.Workflows** `--prerelease` (only if/when multi-agent or workflow orchestration is added)

### Foundry SDK notes
- Use `AIProjectClient` via the supported Foundry integration approach.
- Keep model and endpoint configuration external.

### Persistence
- **Microsoft.EntityFrameworkCore.Sqlite**
- **Microsoft.EntityFrameworkCore.Design**

### Resilience
- **Polly** or built-in HTTP resilience support through `HttpClientFactory`

### Logging
- **Serilog.AspNetCore**
- **Serilog.Sinks.Console**
- optional local file sink if desired

### Observability
- **OpenTelemetry.Extensions.Hosting**
- **Azure.Monitor.OpenTelemetry.AspNetCore**

### MCP
- **ModelContextProtocol** `--prerelease`

### Validation
- **FluentValidation**

### Console client
- **System.CommandLine** or simple manual command parsing
- **Spectre.Console** for a better console UX

### Testing
- **xUnit**
- **FluentAssertions**
- **Microsoft.AspNetCore.Mvc.Testing**
- **NSubstitute** or **Moq**

---

## Suggested Folder Layout for Raven.Core

```text
Raven.Core/
  Api/
    Endpoints/
    Models/
  Application/
    Sessions/
    Chat/
    Jobs/
  AgentRuntime/
    Foundry/
    Prompting/
    Streaming/
    Tools/
  Memory/
    Abstractions/
    Stores/
    Summaries/
  Mcp/
    Clients/
    Policies/
    Registry/
  Skills/
    Abstractions/
    BuiltIn/
  Jobs/
  Infrastructure/
    Persistence/
    Configuration/
    Telemetry/
  Contracts/
  Program.cs
```

---

## Suggested Folder Layout for Raven.Client.Console

```text
Raven.Client.Console/
  Commands/
  Rendering/
  Services/
  Models/
  Program.cs
```

---

## Cross-Cutting Design Rules

1. **Configuration-driven everything**
   - endpoints, model deployment, agent name, MCP servers, and feature flags should all come from configuration.

2. **No direct SDK spread**
   - Azure/Foundry SDK types should stay behind adapters.

3. **Thin transport, strong application core**
   - HTTP endpoints should delegate quickly to application services.

4. **Memory and conversation are separate concerns**
   - chat history, summaries, and user profile memory should not be one undifferentiated blob.

5. **Every tool call is policy-controlled**
   - especially MCP tools and host-side skills.

6. **Streaming is a first-class behavior**
   - do not design only for full-buffer responses.

7. **Observability from the start**
   - add correlation IDs, structured logs, traces, and health checks early.

---

## Implementation Sequence Recommendation

### Phase 1
- Create Raven.Core as ASP.NET Core host
- Create Raven.Client.Console
- Add chat session endpoints
- Add Foundry agent adapter
- Add simple session storage
- Add streaming response support

### Phase 2
- Add skill registry
- Add first local skills
- Add MCP gateway abstraction
- Add hosted background jobs
- Add conversation summaries

### Phase 3
- Add richer memory model
- Add approval flows for sensitive tools
- Add observability dashboards
- Add optional SignalR or richer clients

---

## Specific Suggestions

- Prefer **.NET 8** for both projects.
- Use **Minimal APIs** in Raven.Core unless a controller-based style is already preferred by the team.
- Keep **Raven.Client.Console** disposable and simple; the durable architecture belongs in Raven.Core.
- Add a dedicated **contracts project** early if more than one client is expected soon.
- Introduce **background jobs only for real recurring concerns**; do not invent work just because the host can schedule it.
- Treat **MCP as a privileged boundary**, not just another utility call.
- Plan for **conversation summarization** once sessions become long; do not wait until context windows become a problem.

---

## Recommended First Concrete Project Pair

### Raven.Core
- Type: **ASP.NET Core Web API / Minimal API host**
- Runtime role: local orchestration server for agent operations

### Raven.Client.Console
- Type: **Console app**
- Runtime role: simple human-facing chat shell over HTTP

This pair is the smallest structure that still supports the long-term direction described above.
