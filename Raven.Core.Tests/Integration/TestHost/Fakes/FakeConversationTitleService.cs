using ArkaneSystems.Raven.Core.Application.Chat;

namespace ArkaneSystems.Raven.Core.Tests.Integration.TestHost.Fakes;

// Test double for IConversationTitleService.
// Returns a fixed title so integration tests do not require a real Azure endpoint.
public sealed class FakeConversationTitleService : IConversationTitleService
{
  public Task<string?> GenerateTitleAsync (string userMessage, string agentReply, CancellationToken cancellationToken = default) =>
      Task.FromResult<string?> ("Test conversation");
}
