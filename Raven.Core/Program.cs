using ArkaneSystems.Raven.Core.AgentRuntime;
using ArkaneSystems.Raven.Core.AgentRuntime.Foundry;
using ArkaneSystems.Raven.Core.Api.Endpoints;
using ArkaneSystems.Raven.Core.Application.Sessions;
using ArkaneSystems.Raven.Core.Infrastructure.Filesystem;
using ArkaneSystems.Raven.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Bootstrap logger captures any startup failures before the full Serilog
// pipeline (which needs the host's configuration) is ready.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Raven.Core");

    var builder = WebApplication.CreateBuilder(args);

    // Replace the default Microsoft logging with Serilog. Configuration is
    // read from appsettings.json / appsettings.{Environment}.json so log
    // levels can be tuned without recompiling.
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // Bind the "Foundry" section of appsettings / user-secrets to FoundryOptions
    // so the endpoint, model deployment, and system prompt are all configurable.
    builder.Services.Configure<FoundryOptions>(
        builder.Configuration.GetSection(FoundryOptions.SectionName));

    var workspaceRoot = WorkspacePathResolver.ResolveWorkspaceRoot(builder.Configuration);
    var workspacePaths = new WorkspacePaths(workspaceRoot);
    workspacePaths.EnsureWorkspaceStructure();

    var workspaceIntegrity = workspacePaths.CheckIntegrity();
    if (!workspaceIntegrity.IsHealthy)
    {
        throw new InvalidOperationException(
            $"Workspace integrity checks failed: {string.Join(" | ", workspaceIntegrity.Issues)}");
    }

    builder.Services.AddSingleton<IWorkspacePaths>(workspacePaths);

    var dbPath = workspacePaths.GetSessionDatabasePath();

    Log.Information("Using workspace root {WorkspaceRoot}", workspacePaths.GetWorkspaceRoot());
    Log.Information("Using session database path {DatabasePath}", dbPath);

    // Register a DbContext factory rather than a scoped DbContext directly.
    // The factory lets SqliteSessionStore open and dispose its own short-lived
    // DbContext per operation, which is safe when the store is injected into
    // request handlers that each run on their own thread.
    builder.Services.AddDbContextFactory<RavenDbContext>(options =>
        options.UseSqlite($"Data Source={dbPath}"));

    // The Foundry service is a singleton because it holds the AIAgent instance
    // and an in-memory map of conversationId → AgentSession. These are long-lived
    // objects that should survive for the process lifetime.
    builder.Services.AddSingleton<IAgentConversationService, FoundryAgentConversationService>();

    // The session store is scoped so it aligns with the EF DbContext lifetime
    // used by SqliteSessionStore. Each HTTP request gets its own instance.
    builder.Services.AddScoped<ISessionStore, SqliteSessionStore>();

    var app = builder.Build();

    // Apply any pending EF Core migrations automatically at startup.
    // This creates raven.db and the Sessions table on first run, and applies
    // schema changes on subsequent runs, without requiring a manual migration step.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<RavenDbContext>();
        await db.Database.MigrateAsync();
    }

    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    app.MapChatEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Raven.Core terminated unexpectedly");
}
finally
{
    // Ensure all buffered log entries are flushed before the process exits.
    Log.CloseAndFlush();
}
