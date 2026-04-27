using ArkaneSystems.Raven.Core.AgentRuntime;
using ArkaneSystems.Raven.Core.Infrastructure.Filesystem;
using ArkaneSystems.Raven.Core.Infrastructure.Persistence;

namespace ArkaneSystems.Raven.Core.Tests.Unit.Infrastructure;

public sealed class FileAgentSessionStoreTests
{
  // ------------------------------------------------------------------
  // Helpers
  // ------------------------------------------------------------------

  private static (FileAgentSessionStore Store, string WorkspaceRoot) CreateStore ()
  {
    var workspaceRoot  = Path.Combine (Path.GetTempPath (), "Raven.Core.Tests", Guid.NewGuid ().ToString ("N"));
    var workspacePaths = new WorkspacePaths (workspaceRoot);
    _ = workspacePaths.EnsureWorkspaceStructure ();
    return (new FileAgentSessionStore (workspacePaths), workspaceRoot);
  }

  // ------------------------------------------------------------------
  // Save / Load round-trip
  // ------------------------------------------------------------------

  [Fact]
  public async Task SaveAndLoad_RoundTrips_Correctly ()
  {
    var (sut, root) = CreateStore ();

    try
    {
      const string conversationId = "conv-round-trip";
      const string state          = """{"messages":[{"role":"user","content":"hello"}]}""";

      await sut.SaveAsync (conversationId, state, TestContext.Current.CancellationToken);
      var loaded = await sut.LoadAsync (conversationId, TestContext.Current.CancellationToken);

      Assert.NotNull (loaded);
      Assert.Equal (state, loaded);
    }
    finally
    {
      if (Directory.Exists (root))
        Directory.Delete (root, recursive: true);
    }
  }

  [Fact]
  public async Task LoadAsync_ReturnsNull_WhenNoEntryExists ()
  {
    var (sut, root) = CreateStore ();

    try
    {
      var result = await sut.LoadAsync ("nonexistent-conv", TestContext.Current.CancellationToken);
      Assert.Null (result);
    }
    finally
    {
      if (Directory.Exists (root))
        Directory.Delete (root, recursive: true);
    }
  }

  [Fact]
  public async Task SaveAsync_OverwritesPreviousEntry ()
  {
    var (sut, root) = CreateStore ();

    try
    {
      const string conversationId = "conv-overwrite";

      await sut.SaveAsync (conversationId, """{"v":1}""", TestContext.Current.CancellationToken);
      await sut.SaveAsync (conversationId, """{"v":2}""", TestContext.Current.CancellationToken);

      var loaded = await sut.LoadAsync (conversationId, TestContext.Current.CancellationToken);

      Assert.NotNull (loaded);
      Assert.Equal ("""{"v":2}""", loaded);
    }
    finally
    {
      if (Directory.Exists (root))
        Directory.Delete (root, recursive: true);
    }
  }

  // ------------------------------------------------------------------
  // Delete
  // ------------------------------------------------------------------

  [Fact]
  public async Task DeleteAsync_RemovesEntry_AndReturnsFalse_WhenCalledAgain ()
  {
    var (sut, root) = CreateStore ();

    try
    {
      const string conversationId = "conv-to-delete";

      await sut.SaveAsync (conversationId, """{"state":"ok"}""", TestContext.Current.CancellationToken);

      var first = await sut.DeleteAsync (conversationId, TestContext.Current.CancellationToken);
      Assert.True (first);

      var loaded = await sut.LoadAsync (conversationId, TestContext.Current.CancellationToken);
      Assert.Null (loaded);

      // Idempotent — no error, returns false.
      var second = await sut.DeleteAsync (conversationId, TestContext.Current.CancellationToken);
      Assert.False (second);
    }
    finally
    {
      if (Directory.Exists (root))
        Directory.Delete (root, recursive: true);
    }
  }

  [Fact]
  public async Task DeleteAsync_ReturnsFalse_WhenEntryNeverExisted ()
  {
    var (sut, root) = CreateStore ();

    try
    {
      var result = await sut.DeleteAsync ("never-saved", TestContext.Current.CancellationToken);
      Assert.False (result);
    }
    finally
    {
      if (Directory.Exists (root))
        Directory.Delete (root, recursive: true);
    }
  }

  // ------------------------------------------------------------------
  // Guard-clause validation
  // ------------------------------------------------------------------

  [Theory]
  [InlineData ("")]
  [InlineData (" ")]
  public async Task SaveAsync_Throws_WhenConversationIdIsNullOrWhiteSpace (string invalidId)
  {
    var (sut, root) = CreateStore ();

    try
    {
      await Assert.ThrowsAnyAsync<ArgumentException> (
          () => sut.SaveAsync (invalidId, "{}", TestContext.Current.CancellationToken));
    }
    finally
    {
      if (Directory.Exists (root))
        Directory.Delete (root, recursive: true);
    }
  }

  [Theory]
  [InlineData ("")]
  [InlineData (" ")]
  public async Task LoadAsync_Throws_WhenConversationIdIsNullOrWhiteSpace (string invalidId)
  {
    var (sut, root) = CreateStore ();

    try
    {
      await Assert.ThrowsAnyAsync<ArgumentException> (
          () => sut.LoadAsync (invalidId, TestContext.Current.CancellationToken));
    }
    finally
    {
      if (Directory.Exists (root))
        Directory.Delete (root, recursive: true);
    }
  }
}

public sealed class InMemoryAgentSessionStoreTests
{
  [Fact]
  public async Task SaveAndLoad_RoundTrips_Correctly ()
  {
    var sut = new InMemoryAgentSessionStore ();

    const string conversationId = "conv-1";
    const string state          = """{"messages":[]}""";

    await sut.SaveAsync (conversationId, state, TestContext.Current.CancellationToken);
    var loaded = await sut.LoadAsync (conversationId, TestContext.Current.CancellationToken);

    Assert.Equal (state, loaded);
  }

  [Fact]
  public async Task LoadAsync_ReturnsNull_WhenEntryAbsent ()
  {
    var sut    = new InMemoryAgentSessionStore ();
    var result = await sut.LoadAsync ("missing", TestContext.Current.CancellationToken);
    Assert.Null (result);
  }

  [Fact]
  public async Task SaveAsync_OverwritesPreviousEntry ()
  {
    var sut = new InMemoryAgentSessionStore ();

    await sut.SaveAsync ("c", """{"v":1}""", TestContext.Current.CancellationToken);
    await sut.SaveAsync ("c", """{"v":2}""", TestContext.Current.CancellationToken);

    var loaded = await sut.LoadAsync ("c", TestContext.Current.CancellationToken);
    Assert.Equal ("""{"v":2}""", loaded);
  }

  [Fact]
  public async Task DeleteAsync_RemovesEntry_AndIsIdempotent ()
  {
    var sut = new InMemoryAgentSessionStore ();

    await sut.SaveAsync ("c", "{}", TestContext.Current.CancellationToken);

    Assert.True (await sut.DeleteAsync ("c", TestContext.Current.CancellationToken));
    Assert.Null (await sut.LoadAsync ("c", TestContext.Current.CancellationToken));
    Assert.False (await sut.DeleteAsync ("c", TestContext.Current.CancellationToken));
  }

  [Fact]
  public async Task EntryIsolation_DifferentConversations_DoNotShareState ()
  {
    var sut = new InMemoryAgentSessionStore ();

    await sut.SaveAsync ("c1", """{"id":1}""", TestContext.Current.CancellationToken);
    await sut.SaveAsync ("c2", """{"id":2}""", TestContext.Current.CancellationToken);

    Assert.Equal ("""{"id":1}""", await sut.LoadAsync ("c1", TestContext.Current.CancellationToken));
    Assert.Equal ("""{"id":2}""", await sut.LoadAsync ("c2", TestContext.Current.CancellationToken));
  }
}
