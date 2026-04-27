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

var ravenCoreBaseUrl = builder.Configuration["RavenCore:BaseUrl"];

// Use a sensible default if the configuration value is missing, and validate any explicit value.
// This avoids unhelpful ArgumentNullException/UriFormatException on startup when the setting
// is absent or malformed, while still failing fast for bad explicit configuration.
const string defaultRavenCoreBaseUrl = "http://localhost:5269";

Uri ravenCoreBaseUri;
if (string.IsNullOrWhiteSpace (ravenCoreBaseUrl))
{
  ravenCoreBaseUri = new Uri (defaultRavenCoreBaseUrl, UriKind.Absolute);
}
else if (!Uri.TryCreate (ravenCoreBaseUrl, UriKind.Absolute, out ravenCoreBaseUri!))
{
  throw new InvalidOperationException (
      $"Configuration value 'RavenCore:BaseUrl' is not a valid absolute URI: '{ravenCoreBaseUrl}'.");
}
// Typed HttpClient: the HttpClient instance is configured with the base URL
// from appsettings.json ("RavenCore:BaseUrl") and injected directly into
// RavenApiClient via its constructor. The IHttpClientFactory manages pooling.
builder.Services.AddHttpClient<RavenApiClient> (client =>
    client.BaseAddress = ravenCoreBaseUri);

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

// Wait for Raven.Core to report healthy instead of relying on a fixed startup delay.
await WaitForRavenCoreReadyAsync (ravenCoreBaseUri);

// Parse the optional --resume <sessionId> command-line argument.
// When provided, the REPL skips creating a new session and attaches to the
// existing one instead so the conversation context is preserved.
string? resumeSessionId = ParseResumeArg (args);

// Resolve and run the REPL loop directly — no hosted service wrapper needed
// because this is a single-purpose foreground process.
await host.Services.GetRequiredService<ConsoleLoop> ().RunAsync (resumeSessionId);

// Looks for --resume <id> in the raw args array.
// Returns the session ID if found; otherwise null.
static string? ParseResumeArg (string[] args)
{
  for (int i = 0; i < args.Length - 1; i++)
  {
    if (string.Equals (args[i], "--resume", StringComparison.OrdinalIgnoreCase))
    {
      var id = args[i + 1];
      return string.IsNullOrWhiteSpace (id) ? null : id;
    }
  }

  return null;
}

static async Task WaitForRavenCoreReadyAsync (Uri baseAddress, CancellationToken cancellationToken = default)
{
  var overallTimeout = TimeSpan.FromSeconds (30);
  var probeInterval = TimeSpan.FromMilliseconds (500);
  var perRequestTimeout = TimeSpan.FromSeconds (2);

  using var http = new HttpClient { BaseAddress = baseAddress };

  Exception? lastFailure = null;
  var deadline = DateTimeOffset.UtcNow + overallTimeout;

  while (DateTimeOffset.UtcNow < deadline)
  {
    cancellationToken.ThrowIfCancellationRequested ();

    using var probeCts = CancellationTokenSource.CreateLinkedTokenSource (cancellationToken);
    probeCts.CancelAfter (perRequestTimeout);

    try
    {
      using var response = await http.GetAsync ("/health", probeCts.Token);
      if (response.IsSuccessStatusCode)
      {
        return;
      }

      lastFailure = new HttpRequestException (
          $"Health probe returned unexpected status code {(int)response.StatusCode} ({response.StatusCode}).");
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
      lastFailure = ex;
    }

    await Task.Delay (probeInterval, cancellationToken);
  }

  throw new InvalidOperationException (
      $"Raven.Core at '{baseAddress}' was not ready within {overallTimeout.TotalSeconds:0} seconds.",
      lastFailure);
}
