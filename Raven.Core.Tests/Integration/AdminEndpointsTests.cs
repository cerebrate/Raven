using ArkaneSystems.Raven.Contracts.Admin;
using ArkaneSystems.Raven.Core.Application.Admin;
using ArkaneSystems.Raven.Core.Tests.Integration.TestHost;
using ArkaneSystems.Raven.Core.Tests.Integration.TestHost.Fakes;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace ArkaneSystems.Raven.Core.Tests.Integration;

[Collection(IntegrationTestCollection.Name)]
public sealed class AdminEndpointsTests (RavenCoreWebAppFactory factory)
{
  private readonly RavenCoreWebAppFactory _factory = factory;
  private readonly HttpClient _client = factory.CreateClient();

  private FakeShutdownCoordinator FakeShutdown =>
      this._factory.Services.GetRequiredService<FakeShutdownCoordinator>();

  [Fact]
  public async Task Shutdown_Returns202Accepted ()
  {
    using var shutdownStateScope = new ShutdownStateScope(this.FakeShutdown);

    var response = await this._client.PostAsync("/api/admin/shutdown", content: null, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
  }

  [Fact]
  public async Task Shutdown_ReturnsAcceptedResponseBody ()
  {
    using var shutdownStateScope = new ShutdownStateScope(this.FakeShutdown);

    var response = await this._client.PostAsync("/api/admin/shutdown", content: null, TestContext.Current.CancellationToken);
    _ = response.EnsureSuccessStatusCode();

    var body = await response.Content.ReadFromJsonAsync<AdminCommandResponse>(TestContext.Current.CancellationToken);
    Assert.NotNull(body);
    Assert.False(string.IsNullOrWhiteSpace(body.Message));
  }

  [Fact]
  public async Task Shutdown_CallsCoordinatorWithRestartFalse ()
  {
    using var shutdownStateScope = new ShutdownStateScope(this.FakeShutdown);

    _ = await this._client.PostAsync("/api/admin/shutdown", content: null, TestContext.Current.CancellationToken);

    Assert.True(this.FakeShutdown.IsShutdownRequested);
    Assert.Equal(false, this.FakeShutdown.LastRequestedRestart);
  }

  [Fact]
  public async Task Restart_Returns202Accepted ()
  {
    using var shutdownStateScope = new ShutdownStateScope(this.FakeShutdown);

    var response = await this._client.PostAsync("/api/admin/restart", content: null, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
  }

  [Fact]
  public async Task Restart_ReturnsAcceptedResponseBody ()
  {
    using var shutdownStateScope = new ShutdownStateScope(this.FakeShutdown);

    var response = await this._client.PostAsync("/api/admin/restart", content: null, TestContext.Current.CancellationToken);
    _ = response.EnsureSuccessStatusCode();

    var body = await response.Content.ReadFromJsonAsync<AdminCommandResponse>(TestContext.Current.CancellationToken);
    Assert.NotNull(body);
    Assert.False(string.IsNullOrWhiteSpace(body.Message));
  }

  [Fact]
  public async Task Restart_CallsCoordinatorWithRestartTrue ()
  {
    using var shutdownStateScope = new ShutdownStateScope(this.FakeShutdown);

    _ = await this._client.PostAsync("/api/admin/restart", content: null, TestContext.Current.CancellationToken);

    Assert.True(this.FakeShutdown.IsShutdownRequested);
    Assert.Equal(true, this.FakeShutdown.LastRequestedRestart);
  }
}

// Isolated test for the 503-during-shutdown scenario. Uses the shared collection
// factory and a reset scope so singleton state cannot bleed into other tests.
[Collection(IntegrationTestCollection.Name)]
public sealed class AdminShutdownInProgressTests (RavenCoreWebAppFactory factory)
{
  private readonly RavenCoreWebAppFactory _factory = factory;
  private readonly HttpClient _client = factory.CreateClient();

  private FakeShutdownCoordinator FakeShutdown =>
      this._factory.Services.GetRequiredService<FakeShutdownCoordinator>();

  [Fact]
  public async Task StreamMessage_Returns503_WhenShutdownIsRequested ()
  {
    using var shutdownStateScope = new ShutdownStateScope(this.FakeShutdown);

    await this.FakeShutdown.RequestShutdownAsync(restart: false, TestContext.Current.CancellationToken);

    // Create a session first, then try to stream; the 503 guard should reject the request.
    var sessionResponse = await this._client.PostAsJsonAsync(
        "/api/chat/sessions",
        new { },
        TestContext.Current.CancellationToken);
    _ = sessionResponse.EnsureSuccessStatusCode();
    var sessionPayload = await sessionResponse.Content.ReadFromJsonAsync<ArkaneSystems.Raven.Contracts.Chat.CreateSessionResponse>(TestContext.Current.CancellationToken);
    Assert.NotNull(sessionPayload);

    var response = await this._client.PostAsJsonAsync(
        $"/api/chat/sessions/{sessionPayload.SessionId}/messages/stream",
        new { Content = "hello" },
        TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
  }
}

file sealed class ShutdownStateScope : IDisposable
{
  private readonly FakeShutdownCoordinator _fakeShutdown;

  public ShutdownStateScope (FakeShutdownCoordinator fakeShutdown)
  {
    ArgumentNullException.ThrowIfNull(fakeShutdown);

    this._fakeShutdown = fakeShutdown;
    this._fakeShutdown.Reset();
  }

  public void Dispose () => this._fakeShutdown.Reset();
}

