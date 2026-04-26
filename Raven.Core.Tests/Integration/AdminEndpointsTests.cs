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
    this.FakeShutdown.Reset();

    var response = await this._client.PostAsync("/api/admin/shutdown", content: null, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
  }

  [Fact]
  public async Task Shutdown_ReturnsAcceptedResponseBody ()
  {
    this.FakeShutdown.Reset();

    var response = await this._client.PostAsync("/api/admin/shutdown", content: null, TestContext.Current.CancellationToken);
    _ = response.EnsureSuccessStatusCode();

    var body = await response.Content.ReadFromJsonAsync<AdminCommandResponse>(TestContext.Current.CancellationToken);
    Assert.NotNull(body);
    Assert.False(string.IsNullOrWhiteSpace(body.Message));
  }

  [Fact]
  public async Task Shutdown_CallsCoordinatorWithRestartFalse ()
  {
    this.FakeShutdown.Reset();

    try
    {
      _ = await this._client.PostAsync("/api/admin/shutdown", content: null, TestContext.Current.CancellationToken);

      Assert.True(this.FakeShutdown.IsShutdownRequested);
      Assert.Equal(false, this.FakeShutdown.LastRequestedRestart);
    }
    finally
    {
      this.FakeShutdown.Reset();
    }
  }

  [Fact]
  public async Task Restart_Returns202Accepted ()
  {
    this.FakeShutdown.Reset();

    var response = await this._client.PostAsync("/api/admin/restart", content: null, TestContext.Current.CancellationToken);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
  }

  [Fact]
  public async Task Restart_ReturnsAcceptedResponseBody ()
  {
    this.FakeShutdown.Reset();

    var response = await this._client.PostAsync("/api/admin/restart", content: null, TestContext.Current.CancellationToken);
    _ = response.EnsureSuccessStatusCode();

    var body = await response.Content.ReadFromJsonAsync<AdminCommandResponse>(TestContext.Current.CancellationToken);
    Assert.NotNull(body);
    Assert.False(string.IsNullOrWhiteSpace(body.Message));
  }

  [Fact]
  public async Task Restart_CallsCoordinatorWithRestartTrue ()
  {
    this.FakeShutdown.Reset();

    try
    {
      _ = await this._client.PostAsync("/api/admin/restart", content: null, TestContext.Current.CancellationToken);

      Assert.True(this.FakeShutdown.IsShutdownRequested);
      Assert.Equal(true, this.FakeShutdown.LastRequestedRestart);
    }
    finally
    {
      this.FakeShutdown.Reset();
    }
  }
}

// Isolated test for the 503-during-shutdown scenario. Uses the shared collection
// factory but always resets coordinator state in a finally block so it cannot
// bleed into other tests.
[Collection(IntegrationTestCollection.Name)]
public sealed class AdminShutdownInProgressTests (RavenCoreWebAppFactory factory)
{
  private readonly RavenCoreWebAppFactory _factory = factory;
  private readonly HttpClient _client = factory.CreateClient();

  [Fact]
  public async Task StreamMessage_Returns503_WhenShutdownIsRequested ()
  {
    var fakeShutdown = this._factory.Services.GetRequiredService<FakeShutdownCoordinator>();
    fakeShutdown.Reset();

    await fakeShutdown.RequestShutdownAsync(restart: false, TestContext.Current.CancellationToken);

    try
    {
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
    finally
    {
      fakeShutdown.Reset();
    }
  }
}

