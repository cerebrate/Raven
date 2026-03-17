using ArkaneSystems.Raven.Core.AgentRuntime;
using ArkaneSystems.Raven.Core.AgentRuntime.Foundry;
using ArkaneSystems.Raven.Core.Api.Endpoints;
using ArkaneSystems.Raven.Core.Application.Sessions;
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

    builder.Services.AddSingleton<IAgentConversationService, FoundryAgentConversationService>();
    builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();

    var app = builder.Build();

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
