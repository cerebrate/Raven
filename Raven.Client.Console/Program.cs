using ArkaneSystems.Raven.Client.Console;
using ArkaneSystems.Raven.Client.Console.Models;
using ArkaneSystems.Raven.Client.Console.Rendering;
using ArkaneSystems.Raven.Client.Console.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Use the Generic Host so we get DI, configuration, and logging for free,
// even though this is a console application with no HTTP server.
var builder = Host.CreateApplicationBuilder(args);

// Suppress framework-level log output (HTTP client noise, host lifecycle events)
// so the console stays clean for the user. Application-level errors are shown
// via IConsoleRenderer.ShowError, not the logger.
builder.Logging.SetMinimumLevel (LogLevel.Warning);

// Typed HttpClient: the HttpClient instance is configured with the base URL
// from appsettings.json ("RavenCore:BaseUrl") and injected directly into
// RavenApiClient via its constructor. The IHttpClientFactory manages pooling.
builder.Services.AddHttpClient<RavenApiClient> (client =>
    client.BaseAddress = new Uri (builder.Configuration["RavenCore:BaseUrl"] ?? "http://localhost:5269"));

// SessionState holds the currently active session ID and is shared between
// RavenApiClient calls and ConsoleLoop. Singleton so the same instance is
// injected everywhere.
builder.Services.AddSingleton<SessionState> ();

// IConsoleRenderer abstracts all terminal output. SpectreConsoleRenderer is the
// production implementation; a test double could be substituted here.
builder.Services.AddSingleton<IConsoleRenderer, SpectreConsoleRenderer> ();

// Transient because ConsoleLoop is only ever resolved once per run, and Transient
// is the safest default for a class that is not designed to be reused.
builder.Services.AddTransient<ConsoleLoop> ();

var host = builder.Build();

// Resolve and run the REPL loop directly — no hosted service wrapper needed
// because this is a single-purpose foreground process.
await host.Services.GetRequiredService<ConsoleLoop> ().RunAsync ();