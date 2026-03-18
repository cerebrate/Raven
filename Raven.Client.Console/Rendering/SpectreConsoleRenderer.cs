using ArkaneSystems.Raven.Contracts.Chat;
using Spectre.Console;

namespace ArkaneSystems.Raven.Client.Console.Rendering;

// Spectre.Console implementation of IConsoleRenderer.
// Spectre.Console handles ANSI colour codes, markup escaping, tables, and
// FigletText — all the things that would be painful with plain Console.Write.
// Markup syntax: [colour]text[/] — see https://spectreconsole.net/markup
public class SpectreConsoleRenderer : IConsoleRenderer
{
    public void ShowBanner()
    {
        // FigletText renders large ASCII-art text using a built-in font.
        AnsiConsole.Write(
            new FigletText("Raven")
                .Centered()
                .Color(Color.SteelBlue1));

        // Rule draws a horizontal line with optional centred label.
        AnsiConsole.Write(
            new Rule("[grey]AI Assistant[/]")
                .RuleStyle("grey")
                .Centered());

        AnsiConsole.WriteLine();
    }

    public void ShowSessionStarted(string sessionId)
    {
        AnsiConsole.MarkupLine($"[grey]Session:[/] [dim]{sessionId}[/]");
        AnsiConsole.MarkupLine("[grey]Type [/][steelblue1]/exit[/][grey] to quit, [/][steelblue1]/help[/][grey] for commands.[/]");
        AnsiConsole.WriteLine();
    }

    public void ShowHelp()
    {
        var table = new Table()
            .BorderStyle(Style.Parse("grey"))
            .AddColumn(new TableColumn("[steelblue1]Command[/]").NoWrap())
            .AddColumn(new TableColumn("[grey]Description[/]"));

        table.AddRow("[steelblue1]/new[/]",     "Start a new session");
        table.AddRow("[steelblue1]/history[/]", "Show current session info");
        table.AddRow("[steelblue1]/help[/]",    "Show this help");
        table.AddRow("[steelblue1]/exit[/]",    "End the session and quit");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    public void WriteUserPrompt()
    {
        // Markup (not MarkupLine) so the cursor stays on the same line as the prompt.
        AnsiConsole.Markup("[steelblue1]>[/] ");
    }

    public void BeginResponse()
    {
        // Print the "Raven: " label; the streaming chunks follow on the same line.
        AnsiConsole.Markup("[steelblue1_1]Raven:[/] ");
    }

    public void WriteChunk(string chunk)
    {
        // AnsiConsole.Write (not MarkupLine) — chunks are plain text, not markup,
        // so we do not attempt to interpret square brackets in the agent's reply.
        AnsiConsole.Write(chunk);
    }

    public void EndResponse()
    {
        // Two newlines: one to end the response line, one to add visual spacing
        // before the next prompt.
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
    }

    public void ShowError(string message)
    {
        // Markup.Escape ensures any brackets in the exception message are treated
        // as literal text rather than Spectre markup tags.
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
        AnsiConsole.WriteLine();
    }

    public void ShowGoodbye()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Goodbye.[/]");
    }

    public void ShowSessionInfo(SessionInfoResponse info)
    {
        var table = new Table()
            .BorderStyle(Style.Parse("grey"))
            .AddColumn(new TableColumn("[grey]Property[/]").NoWrap())
            .AddColumn(new TableColumn("[grey]Value[/]"));

        table.AddRow("Session ID",    $"[dim]{Markup.Escape(info.SessionId)}[/]");
        table.AddRow("Started",       $"[dim]{info.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}[/]");
        table.AddRow("Last activity", info.LastActivityAt.HasValue
            ? $"[dim]{info.LastActivityAt.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}[/]"
            : "[dim]—[/]");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    public void ShowNewSession(string oldSessionId, string newSessionId)
    {
        AnsiConsole.MarkupLine($"[grey]Previous session [dim]{Markup.Escape(oldSessionId)}[/] closed.[/]");
        AnsiConsole.MarkupLine($"[grey]New session:[/] [dim]{Markup.Escape(newSessionId)}[/]");
        AnsiConsole.WriteLine();
    }
}
