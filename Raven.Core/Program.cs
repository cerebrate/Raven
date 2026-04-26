using ArkaneSystems.Raven.Core.AgentRuntime;
using ArkaneSystems.Raven.Core.AgentRuntime.Foundry;
using ArkaneSystems.Raven.Core.Api.Endpoints;
using ArkaneSystems.Raven.Core.Application.Admin;
using ArkaneSystems.Raven.Core.Application.Chat;
using ArkaneSystems.Raven.Core.Application.Sessions;
using ArkaneSystems.Raven.Core.Bus.Contracts;
using ArkaneSystems.Raven.Core.Bus.Dispatch;
using ArkaneSystems.Raven.Core.Bus.Handlers;
using ArkaneSystems.Raven.Core.Infrastructure.Filesystem;
using ArkaneSystems.Raven.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Bootstrap logger captures any startup failures before the full Serilog
// pipeline (which needs the host's configuration) is ready.
Log.Logger = new LoggerConfiguration ()
    .WriteTo.Console ()
    .CreateBootstrapLogger ();

try
{
  Log.Information ("Starting Raven.Core");

  var builder = WebApplication.CreateBuilder(args);

  // Replace the default Microsoft logging with Serilog. Configuration is
  // read from appsettings.json / appsettings.{Environment}.json so log
  // levels can be tuned without recompiling.
  _ = builder.Host.UseSerilog ((context, services, configuration) => configuration
      .ReadFrom.Configuration (context.Configuration)
      .ReadFrom.Services (services)
      .Enrich.FromLogContext ());

  // Bind the "Foundry" section of appsettings / user-secrets to FoundryOptions
  // so the endpoint, model deployment, and system prompt are all configurable.
  _ = builder.Services.Configure<FoundryOptions> (
      builder.Configuration.GetSection (FoundryOptions.SectionName));
  _ = builder.Services.Configure<BusDispatchOptions> (
      builder.Configuration.GetSection (BusDispatchOptions.SectionName));

  var workspaceRoot = WorkspacePathResolver.ResolveWorkspaceRoot(builder.Configuration);
  Log.Information ("Startup checkpoint: workspace initialization starting at {WorkspaceRoot}", workspaceRoot);

  var workspacePaths = new WorkspacePaths(workspaceRoot);
  var initializationReport = workspacePaths.EnsureWorkspaceStructure();
  Log.Information (
      "Startup checkpoint: workspace initialization completed (CreatedDirectories: {CreatedCount}, ExistingDirectories: {ExistingCount}, TotalDirectories: {TotalCount})",
      initializationReport.CreatedDirectories.Count,
      initializationReport.ExistingDirectories.Count,
      initializationReport.TotalDirectories);

  Log.Information ("Startup checkpoint: workspace integrity check starting");
  var workspaceIntegrity = workspacePaths.CheckIntegrity();
  Log.Information (
      "Startup checkpoint: workspace integrity check completed (Healthy: {IsHealthy}, MissingDirectories: {MissingCount}, WriteProbeSucceeded: {WriteProbeSucceeded})",
      workspaceIntegrity.IsHealthy,
      workspaceIntegrity.MissingDirectories.Count,
      workspaceIntegrity.WriteProbeSucceeded);
  if (!workspaceIntegrity.IsHealthy)
  {
    var missingDirectories = workspaceIntegrity.MissingDirectories.Count == 0
      ? "none"
      : string.Join (", ", workspaceIntegrity.MissingDirectories);

    var writeProbeError = string.IsNullOrWhiteSpace(workspaceIntegrity.WriteProbeError)
      ? "none"
      : workspaceIntegrity.WriteProbeError;

    throw new InvalidOperationException (
        $"Workspace integrity checks failed. MissingDirectories: {missingDirectories}. WriteProbeError: {writeProbeError}");
  }

  _ = builder.Services.AddSingleton<IWorkspacePaths> (workspacePaths);

  var dbPath = workspacePaths.GetSessionDatabasePath();

  Log.Information ("Using workspace root {WorkspaceRoot}", workspacePaths.GetWorkspaceRoot ());
  Log.Information ("Using session database path {DatabasePath}", dbPath);

  // Use a bounded SQLite busy timeout so migration waits briefly for a lock,
  // then fails with a clear exception instead of stalling startup indefinitely.
  var sqliteConnectionString = $"Data Source={dbPath};Default Timeout=5;";
  Log.Information ("Startup checkpoint: configuring SQLite connection (Default Timeout: {DefaultTimeoutSeconds}s)", 5);

  // Register a DbContext factory rather than a scoped DbContext directly.
  // The factory lets SqliteSessionStore open and dispose its own short-lived
  // DbContext per operation, which is safe when the store is injected into
  // request handlers that each run on their own thread.
  _ = builder.Services.AddDbContextFactory<RavenDbContext> (options =>
      options.UseSqlite (sqliteConnectionString));

  // The Foundry service is a singleton because it holds the AIAgent instance
  // and an in-memory map of conversationId → AgentSession. These are long-lived
  // objects that should survive for the process lifetime.
  _ = builder.Services.AddSingleton<IAgentConversationService, FoundryAgentConversationService> ();

  // The session store is scoped so it aligns with the EF DbContext lifetime
  // used by SqliteSessionStore. Each HTTP request gets its own instance.
  _ = builder.Services.AddScoped<ISessionStore, SqliteSessionStore> ();
  _ = builder.Services.AddSingleton<ISessionEventLog, FileSessionEventLog> ();
  _ = builder.Services.AddScoped<IChatApplicationService, ChatApplicationService> ();
  _ = builder.Services.AddScoped<IChatStreamBroker, ChatStreamBroker> ();

  _ = builder.Services.AddSingleton<IResponseStreamEventHub, InMemoryResponseStreamEventHub> ();
  _ = builder.Services.AddSingleton<ISessionNotificationHub, InMemorySessionNotificationHub> ();
  _ = builder.Services.AddSingleton<IMessageTypeRegistry> (_ =>
  {
    var registry = new InMemoryMessageTypeRegistry();
    registry.Register ("chat.response.started.v1", typeof (ResponseStreamEventEnvelope));
    registry.Register ("chat.response.delta.v1", typeof (ResponseStreamEventEnvelope));
    registry.Register ("chat.response.completed.v1", typeof (ResponseStreamEventEnvelope));
    registry.Register ("chat.response.failed.v1", typeof (ResponseStreamEventEnvelope));
    return registry;
  });
  _ = builder.Services.AddSingleton<IMessageHandler<ResponseStreamEventEnvelope>, ResponseStreamEventForwardingHandler> ();
  _ = builder.Services.AddSingleton<IDeadLetterSink, LoggingDeadLetterSink> ();
  _ = builder.Services.AddSingleton<InProcMessageBus> ();
  _ = builder.Services.AddSingleton<IMessageBus> (sp => sp.GetRequiredService<InProcMessageBus> ());
  _ = builder.Services.AddHostedService (sp => sp.GetRequiredService<InProcMessageBus> ());

  // ShutdownCoordinator is singleton so it holds stable state across requests
  // and can be queried cheaply by every streaming endpoint.
  _ = builder.Services.AddSingleton<IShutdownCoordinator, ShutdownCoordinator> ();

  var app = builder.Build();

  // Apply any pending EF Core migrations automatically at startup.
  // This creates raven.db and the Sessions table on first run, and applies
  // schema changes on subsequent runs, without requiring a manual migration step.
  using (var scope = app.Services.CreateScope ())
  {
    var db = scope.ServiceProvider.GetRequiredService<RavenDbContext>();
    db.Database.SetCommandTimeout (TimeSpan.FromSeconds (15));

    using var migrationCts = new CancellationTokenSource (TimeSpan.FromSeconds (20));

    Log.Information (
        "Startup checkpoint: database migration check starting (CommandTimeout: {CommandTimeoutSeconds}s, OverallTimeout: {OverallTimeoutSeconds}s)",
        15,
        20);

    try
    {
      var pendingMigrations = await db.Database.GetPendingMigrationsAsync (migrationCts.Token);
      var pendingMigrationList = pendingMigrations.ToList();

      Log.Information (
          "Startup checkpoint: database migration check completed (PendingMigrations: {PendingMigrationCount})",
          pendingMigrationList.Count);

      if (pendingMigrationList.Count > 0)
      {
        Log.Information ("Startup checkpoint: database migration applying pending migrations");
        await db.Database.MigrateAsync (migrationCts.Token);
        Log.Information ("Startup checkpoint: database migration completed");
      }
      else
      {
        Log.Information ("Startup checkpoint: database migration skipped (no pending migrations)");
      }
    }
    catch (OperationCanceledException ex) when (migrationCts.IsCancellationRequested)
    {
      throw new TimeoutException (
          $"Database migration timed out after 20 seconds. The SQLite database at '{dbPath}' may be locked by another process.",
          ex);
    }
    catch (Exception ex)
    {
      Log.Error (ex, "Startup checkpoint: database migration failed for {DatabasePath}", dbPath);
      throw;
    }
  }

  _ = app.MapGet ("/health", () => Results.Ok (new { status = "ok" }));
  _ = app.MapChatEndpoints ();
  _ = app.MapAdminEndpoints ();

  Log.Information ("Startup checkpoint: entering app.Run (Kestrel should begin listening)");
  app.Run ();
}
catch (Exception ex)
{
  Log.Fatal (ex, "Raven.Core terminated unexpectedly");
  throw;
}
finally
{
  // Ensure all buffered log entries are flushed before the process exits.
  Log.CloseAndFlush ();
}