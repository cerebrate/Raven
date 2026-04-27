#region header

// Raven.Core - FoundryAgentConversationService.cs
// 
// Alistair J. R. Young
// Arkane Systems
// 
// Copyright Arkane Systems 2012-2026.  All rights reserved.
// 
// Created: 2026-04-27 12:05 PM

#endregion

#region using

using Azure.AI.OpenAI;
using Azure.Identity;
using JetBrains.Annotations;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

#endregion

namespace ArkaneSystems.Raven.Core.AgentRuntime.Foundry;

// Concrete implementation of IAgentConversationService that talks to a model
// deployed in Microsoft Foundry via the Azure OpenAI SDK + Microsoft.Agents.AI.
//
// Lifetime: Singleton. The AIAgent and the session dictionary are long-lived
// objects that should be shared for the lifetime of the process.
//
// Session persistence:
//   After every successful message exchange the current AgentSession is
//   serialized via AIAgent.SerializeSessionAsync and written to
//   IAgentSessionStore.  On a process restart the in-memory dictionary is
//   empty; when a conversationId is not found in the dictionary the service
//   transparently attempts to restore the session from the store before
//   throwing ConversationNotFoundException.  This means callers above this
//   layer (ChatApplicationService) see uninterrupted behaviour across
//   server restarts — stale-session errors only occur for conversations
//   whose persisted state has been explicitly deleted or was never saved.
public class FoundryAgentConversationService : IAgentConversationService
{
  // JsonSerializerOptions used consistently across serialize/deserialize calls.
  // Web defaults (camelCase, case-insensitive) match the SDK's own preferences.
  // Explicitly setting TypeInfoResolver keeps serialization working when
  // reflection-based metadata is disabled by runtime defaults or trimming.
  private static readonly JsonSerializerOptions SerializerOptions =
    new (JsonSerializerDefaults.Web) { WriteIndented = false, TypeInfoResolver = new DefaultJsonTypeInfoResolver () };

  public FoundryAgentConversationService (IOptions<FoundryOptions>                 options,
                                          IAgentSessionStore                       sessionStore,
                                          ILogger<FoundryAgentConversationService> logger)
  {
    FoundryOptions opts = options.Value;

    this._sessionStore = sessionStore;
    this._logger       = logger;

    // Build the agent from the configured Azure OpenAI endpoint using
    // DefaultAzureCredential, which will use the logged-in Azure CLI
    // account in development and managed identity in production.
    this._agent = new AzureOpenAIClient (endpoint: new Uri (opts.Endpoint), credential: new DefaultAzureCredential ())
                 .GetChatClient (opts.DeploymentName)
                 .AsAIAgent (instructions: opts.SystemPrompt,
                             name: opts.AgentName);
  }

  // The AIAgent wraps the Azure OpenAI chat client and holds the configured
  // system prompt and agent name. It is stateless with respect to individual
  // conversations — session state lives in AgentSession objects below.
  private readonly AIAgent _agent;

  private readonly ILogger<FoundryAgentConversationService> _logger;

  // Maps our internal conversationId (a Guid string we generate) to the
  // Foundry AgentSession object, which holds the conversation thread state.
  // ConcurrentDictionary is used because requests can arrive concurrently.
  private readonly ConcurrentDictionary<string, AgentSession> _sessions = new ();

  // Persists serialized AgentSession state so sessions survive restarts.
  private readonly IAgentSessionStore _sessionStore;

  public async Task<string> CreateConversationAsync ()
  {
    // Ask the agent to create a new conversation thread (AgentSession).
    // We then generate our own conversationId to use as the key so we
    // are not coupled to whatever internal ID Foundry uses.
    AgentSession session        = await this._agent.CreateSessionAsync ();
    string       conversationId = Guid.NewGuid ().ToString ();
    this._sessions[conversationId] = session;

    // Persist the initial (empty) session immediately so it is restorable
    // even before any messages are sent.
    await this.PersistSessionAsync (conversationId: conversationId, session: session, cancellationToken: CancellationToken.None);

    return conversationId;
  }

  public async Task<string> SendMessageAsync (string conversationId, string content)
  {
    AgentSession session = await this.GetOrRestoreSessionAsync (conversationId);

    // RunAsync sends the message to Foundry and waits for the full response.
    // .Text extracts the plain-text content from the AgentResponse.
    string reply = (await this._agent.RunAsync (message: content, session: session)).Text;

    // Persist the updated session state after a successful exchange so the
    // next restart can resume from this point.
    await this.PersistSessionAsync (conversationId: conversationId, session: session, cancellationToken: CancellationToken.None);

    return reply;
  }

  public async IAsyncEnumerable<string> StreamMessageAsync (string                                     conversationId,
                                                            string                                     content,
                                                            [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    AgentSession session =
      await this.GetOrRestoreSessionAsync (conversationId: conversationId, cancellationToken: cancellationToken);

    // RunStreamingAsync returns an IAsyncEnumerable of incremental update objects.
    // We yield only updates that carry text — some updates are metadata/control frames
    // with an empty Text property, which we skip to avoid writing blank SSE lines.
    // [EnumeratorCancellation] ensures the CancellationToken is wired through
    // correctly when the caller cancels iteration.
    //
    // streamCompleted is set only after the foreach exits normally; the finally
    // block then persists the updated session.  Persistence is intentionally
    // skipped when the stream fails mid-way: the SDK may have left the
    // AgentSession in a partially-updated state (e.g. a message appended but
    // no assistant response committed), so persisting it would corrupt the
    // conversation history.  The in-memory session remains in the partially-
    // updated state, but the on-disk copy retains the last fully-committed
    // state — which is the safe baseline for any future restart.
    bool streamCompleted = false;

    try
    {
      await foreach (AgentResponseUpdate update in this._agent
                                                       .RunStreamingAsync (message: content,
                                                                           session: session,
                                                                           cancellationToken: cancellationToken)
                                                       .WithCancellation (cancellationToken))
      {
        if (!string.IsNullOrEmpty (update.Text))
        {
          yield return update.Text;
        }
      }

      streamCompleted = true;
    }
    finally
    {
      if (streamCompleted)
      {
        // Use CancellationToken.None so a cancelled request token does not
        // prevent the session state from being saved after a successful stream.
        await this.PersistSessionAsync (conversationId: conversationId,
                                        session: session,
                                        cancellationToken: CancellationToken.None);
      }
    }
  }

  // Returns the in-memory AgentSession for the given conversationId.
  // If the session is not in memory (e.g. after a process restart), attempts
  // to restore it from the persistent store.  Throws ConversationNotFoundException
  // only when no persisted state exists for the given conversationId.
  private async ValueTask<AgentSession> GetOrRestoreSessionAsync (string            conversationId,
                                                                  CancellationToken cancellationToken = default)
  {
    if (this._sessions.TryGetValue (key: conversationId, value: out AgentSession? session))
    {
      return session;
    }

    string? serialized = await this._sessionStore.LoadAsync (conversationId: conversationId, cancellationToken: cancellationToken);

    if (serialized is null)
    {
      throw new ConversationNotFoundException (conversationId);
    }

    JsonElement jsonElement =
      JsonSerializer.Deserialize<JsonElement> (json: serialized, options: FoundryAgentConversationService.SerializerOptions);
    AgentSession restoredSession = await this._agent.DeserializeSessionAsync (serializedState: jsonElement,
                                                                              jsonSerializerOptions: FoundryAgentConversationService
                                                                               .SerializerOptions,
                                                                              cancellationToken: cancellationToken);

    // Store under the conversationId so subsequent calls within this request
    // and within the same process lifetime hit the in-memory path.
    this._sessions[conversationId] = restoredSession;

    this._logger.LogInformation (message: "Agent session for conversation {ConversationId} restored from persisted state.",
                                 conversationId);

    return restoredSession;
  }

  // Serializes the given AgentSession and writes it to the persistent store.
  private async Task PersistSessionAsync (string            conversationId,
                                          AgentSession      session,
                                          CancellationToken cancellationToken)
  {
    JsonElement jsonElement = await this._agent.SerializeSessionAsync (session: session,
                                                                       jsonSerializerOptions: FoundryAgentConversationService
                                                                        .SerializerOptions,
                                                                       cancellationToken: cancellationToken);
    string json = JsonSerializer.Serialize (value: jsonElement, options: FoundryAgentConversationService.SerializerOptions);
    await this._sessionStore.SaveAsync (conversationId: conversationId,
                                        serializedState: json,
                                        cancellationToken: cancellationToken);
  }
}