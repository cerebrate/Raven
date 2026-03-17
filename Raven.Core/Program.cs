using ArkaneSystems.Raven.Core.AgentRuntime;
using ArkaneSystems.Raven.Core.AgentRuntime.Foundry;
using ArkaneSystems.Raven.Core.Api.Endpoints;
using ArkaneSystems.Raven.Core.Application.Sessions;
using ArkaneSystems.Raven.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Raven.Core");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.Configure<FoundryOptions>(
        builder.Configuration.GetSection(FoundryOptions.SectionName));

    var dbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Raven", "raven.db");
    Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

    builder.Services.AddDbContextFactory<RavenDbContext>(options =>
        options.UseSqlite($"Data Source={dbPath}"));

    builder.Services.AddSingleton<IAgentConversationService, FoundryAgentConversationService>();
    builder.Services.AddScoped<ISessionStore, SqliteSessionStore>();

    var app = builder.Build();

    // Auto-migrate on startup
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
    Log.CloseAndFlush();
}
