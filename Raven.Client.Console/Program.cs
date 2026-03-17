using ArkaneSystems.Raven.Client.Console;
using ArkaneSystems.Raven.Client.Console.Models;
using ArkaneSystems.Raven.Client.Console.Rendering;
using ArkaneSystems.Raven.Client.Console.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddHttpClient<RavenApiClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["RavenCore:BaseUrl"] ?? "http://localhost:5269"));

builder.Services.AddSingleton<SessionState>();
builder.Services.AddSingleton<IConsoleRenderer, SpectreConsoleRenderer>();
builder.Services.AddTransient<ConsoleLoop>();

var host = builder.Build();

await host.Services.GetRequiredService<ConsoleLoop>().RunAsync();
