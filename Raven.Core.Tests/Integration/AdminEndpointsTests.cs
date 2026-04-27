#region header

// Raven.Core.Tests - AdminEndpointsTests.cs
// 
// Alistair J. R. Young
// Arkane Systems
// 
// Copyright Arkane Systems 2012-2026.  All rights reserved.
// 
// Created: 2026-04-26 9:45 AM

#endregion

#region using

using ArkaneSystems.Raven.Contracts.Admin;
using ArkaneSystems.Raven.Contracts.Chat;
using ArkaneSystems.Raven.Core.Tests.Integration.TestHost;
using ArkaneSystems.Raven.Core.Tests.Integration.TestHost.Fakes;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

#endregion

namespace ArkaneSystems.Raven.Core.Tests.Integration;

[Collection (IntegrationTestCollection.Name)]
public sealed class AdminEndpointsTests (RavenCoreWebAppFactory factory)
{
  private readonly HttpClient             _client  = factory.CreateClient ();
  private readonly RavenCoreWebAppFactory _factory = factory;

  private FakeShutdownCoordinator FakeShutdown => this._factory.Services.GetRequiredService<FakeShutdownCoordinator> ();

  [Fact]
  public async Task Shutdown_Returns202Accepted ()
  {
    using ShutdownStateScope shutdownStateScope = new ShutdownStateScope (this.FakeShutdown);

    HttpResponseMessage response = await this._client.PostAsync (requestUri: "/api/admin/shutdown",
                                                                 content: null,
                                                                 cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.Accepted, actual: response.StatusCode);
  }

  [Fact]
  public async Task Shutdown_ReturnsAcceptedResponseBody ()
  {
    using ShutdownStateScope shutdownStateScope = new ShutdownStateScope (this.FakeShutdown);

    HttpResponseMessage response = await this._client.PostAsync (requestUri: "/api/admin/shutdown",
                                                                 content: null,
                                                                 cancellationToken: TestContext.Current.CancellationToken);
    _ = response.EnsureSuccessStatusCode ();

    AdminCommandResponse? body =
      await response.Content.ReadFromJsonAsync<AdminCommandResponse> (TestContext.Current.CancellationToken);
    Assert.NotNull (body);
    Assert.False (string.IsNullOrWhiteSpace (body.Message));
  }

  [Fact]
  public async Task Shutdown_CallsCoordinatorWithRestartFalse ()
  {
    using ShutdownStateScope shutdownStateScope = new ShutdownStateScope (this.FakeShutdown);

    _ = await this._client.PostAsync (requestUri: "/api/admin/shutdown",
                                      content: null,
                                      cancellationToken: TestContext.Current.CancellationToken);

    Assert.True (this.FakeShutdown.IsShutdownRequested);
    Assert.Equal (expected: false, actual: this.FakeShutdown.LastRequestedRestart);
  }

  [Fact]
  public async Task Restart_Returns202Accepted ()
  {
    using ShutdownStateScope shutdownStateScope = new ShutdownStateScope (this.FakeShutdown);

    HttpResponseMessage response = await this._client.PostAsync (requestUri: "/api/admin/restart",
                                                                 content: null,
                                                                 cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.Accepted, actual: response.StatusCode);
  }

  [Fact]
  public async Task Restart_ReturnsAcceptedResponseBody ()
  {
    using ShutdownStateScope shutdownStateScope = new ShutdownStateScope (this.FakeShutdown);

    HttpResponseMessage response = await this._client.PostAsync (requestUri: "/api/admin/restart",
                                                                 content: null,
                                                                 cancellationToken: TestContext.Current.CancellationToken);
    _ = response.EnsureSuccessStatusCode ();

    AdminCommandResponse? body =
      await response.Content.ReadFromJsonAsync<AdminCommandResponse> (TestContext.Current.CancellationToken);
    Assert.NotNull (body);
    Assert.False (string.IsNullOrWhiteSpace (body.Message));
  }

  [Fact]
  public async Task Restart_CallsCoordinatorWithRestartTrue ()
  {
    using ShutdownStateScope shutdownStateScope = new ShutdownStateScope (this.FakeShutdown);

    _ = await this._client.PostAsync (requestUri: "/api/admin/restart",
                                      content: null,
                                      cancellationToken: TestContext.Current.CancellationToken);

    Assert.True (this.FakeShutdown.IsShutdownRequested);
    Assert.Equal (expected: true, actual: this.FakeShutdown.LastRequestedRestart);
  }
}

// Isolated test for the 503-during-shutdown scenario. Uses the shared collection
// factory and a reset scope so singleton state cannot bleed into other tests.
[Collection (IntegrationTestCollection.Name)]
public sealed class AdminShutdownInProgressTests (RavenCoreWebAppFactory factory)
{
  private readonly HttpClient             _client  = factory.CreateClient ();
  private readonly RavenCoreWebAppFactory _factory = factory;

  private FakeShutdownCoordinator FakeShutdown => this._factory.Services.GetRequiredService<FakeShutdownCoordinator> ();

  [Fact]
  public async Task StreamMessage_Returns503_WhenShutdownIsRequested ()
  {
    using ShutdownStateScope shutdownStateScope = new ShutdownStateScope (this.FakeShutdown);

    await this.FakeShutdown.RequestShutdownAsync (restart: false, cancellationToken: TestContext.Current.CancellationToken);

    // Create a session first, then try to stream; the 503 guard should reject the request.
    HttpResponseMessage sessionResponse = await this._client.PostAsJsonAsync (requestUri: "/api/chat/sessions",
                                                                              value: new { },
                                                                              cancellationToken: TestContext.Current
                                                                                                            .CancellationToken);
    _ = sessionResponse.EnsureSuccessStatusCode ();
    CreateSessionResponse? sessionPayload =
      await sessionResponse.Content.ReadFromJsonAsync<CreateSessionResponse> (TestContext.Current.CancellationToken);
    Assert.NotNull (sessionPayload);

    HttpResponseMessage response =
      await this._client.PostAsJsonAsync (requestUri: $"/api/chat/sessions/{sessionPayload.SessionId}/messages/stream",
                                          value: new { Content = "hello" },
                                          cancellationToken: TestContext.Current.CancellationToken);

    Assert.Equal (expected: HttpStatusCode.ServiceUnavailable, actual: response.StatusCode);
  }
}

file sealed class ShutdownStateScope : IDisposable
{
  public ShutdownStateScope (FakeShutdownCoordinator fakeShutdown)
  {
    ArgumentNullException.ThrowIfNull (fakeShutdown);

    this._fakeShutdown = fakeShutdown;
    this._fakeShutdown.Reset ();
  }

  private readonly FakeShutdownCoordinator _fakeShutdown;

  public void Dispose () => this._fakeShutdown.Reset ();
}