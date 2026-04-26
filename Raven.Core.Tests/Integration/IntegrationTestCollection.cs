using ArkaneSystems.Raven.Core.Tests.Integration.TestHost;

namespace ArkaneSystems.Raven.Core.Tests.Integration;

// Shared xUnit collection fixture that ensures all integration test classes that
// require a running test host share the same WebApplicationFactory instance.
// This prevents the Serilog static bootstrap logger from being initialised more
// than once, which would cause "The logger is already frozen" errors when tests
// run in parallel.
[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<RavenCoreWebAppFactory>
{
  public const string Name = "Integration";
}
