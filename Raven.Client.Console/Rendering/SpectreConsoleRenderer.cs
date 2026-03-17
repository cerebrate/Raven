using Spectre.Console;

namespace ArkaneSystems.Raven.Client.Console.Rendering;

public class SpectreConsoleRenderer : IConsoleRenderer
{
    public void ShowBanner()
    {
        AnsiConsole.Write(
            new FigletText("Raven")
                .Centered()
                .Color(Color.SteelBlue1));

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

        table.AddRow("[steelblue1]/exit[/]", "End the session and quit");
        table.AddRow("[steelblue1]/help[/]", "Show this help");

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    public void WriteUserPrompt()
    {
        AnsiConsole.Markup("[steelblue1]>[/] ");
    }

    public void BeginResponse()
    {
        AnsiConsole.Markup("[steelblue1_1]Raven:[/] ");
    }

    public void WriteChunk(string chunk)
    {
        AnsiConsole.Write(chunk);
    }

    public void EndResponse()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
    }

    public void ShowError(string message)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
        AnsiConsole.WriteLine();
    }

    public void ShowGoodbye()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Goodbye.[/]");
    }
}
