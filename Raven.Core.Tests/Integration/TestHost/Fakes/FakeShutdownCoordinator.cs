#region header

// Raven.Core.Tests - FakeShutdownCoordinator.cs
// 
// Alistair J. R. Young
// Arkane Systems
// 
// Copyright Arkane Systems 2012-2026.  All rights reserved.
// 
// Created: 2026-04-26 9:45 AM

#endregion

#region using

using ArkaneSystems.Raven.Core.Application.Admin;
using JetBrains.Annotations;

#endregion

namespace ArkaneSystems.Raven.Core.Tests.Integration.TestHost.Fakes;

// Test double for IShutdownCoordinator. Records calls without actually stopping
// the application, so integration tests can exercise the admin endpoints safely.
public sealed class FakeShutdownCoordinator : IShutdownCoordinator
{
  public bool IsShutdownRequested { get; private set; }

  // The restart flag passed to the most recent RequestShutdownAsync call.
  public bool? LastRequestedRestart { get; private set; }

  public Task RequestShutdownAsync (bool restart, CancellationToken cancellationToken = default)
  {
    this.IsShutdownRequested  = true;
    this.LastRequestedRestart = restart;

    return Task.CompletedTask;
  }

  public void Reset ()
  {
    this.IsShutdownRequested  = false;
    this.LastRequestedRestart = null;
  }
}