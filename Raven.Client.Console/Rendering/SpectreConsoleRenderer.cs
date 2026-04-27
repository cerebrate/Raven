using ArkaneSystems.Raven.Contracts.Chat;
using Spectre.Console;

namespace ArkaneSystems.Raven.Client.Console.Rendering;

// Spectre.Console implementation of IConsoleRenderer.
// Spectre.Console handles ANSI colour codes, markup escaping, tables, and
// FigletText — all the things that would be painful with plain Console.Write.
// Markup syntax: [colour]text[/] — see https://spectreconsole.net/markup
public class SpectreConsoleRenderer : IConsoleRenderer
{
  public void ShowBanner ()
  {
    // FigletText renders large ASCII-art text using a built-in font.
    AnsiConsole.Write (
        new FigletText ("Raven")
            .Centered ()
            .Color (Color.SteelBlue1));

    // Rule draws a horizontal line with optional centred label.
    AnsiConsole.Write (
        new Rule ("[grey]AI Assistant[/]")
            .RuleStyle ("grey")
            .Centered ());

    AnsiConsole.WriteLine ();
  }

  public void ShowSessionStarted (string sessionId, bool isResumed = false)
  {
    var verb = isResumed ? "Resumed session" : "Session";
    AnsiConsole.MarkupLine ($"[grey]{verb}:[/] [dim]{sessionId}[/]");
    AnsiConsole.MarkupLine ("[grey]Type [/][steelblue1]/exit[/][grey] to quit, [/][steelblue1]/help[/][grey] for commands, [/][yellow]/admin:shutdown[/][grey] or [/][yellow]/admin:restart[/][grey] to manage the server.[/]");
    AnsiConsole.WriteLine ();
  }

  public void ShowHelp ()
  {
    var table = new Table()
            .BorderStyle(Style.Parse("grey"))
            .AddColumn(new TableColumn("[steelblue1]Command[/]").NoWrap())
            .AddColumn(new TableColumn("[grey]Description[/]"));

    // Session commands
    table.AddRow ("[steelblue1]/new[/]", "Start a new session");
    table.AddRow ("[steelblue1]/sessions[/]", "List resumable sessions");
    table.AddRow ("[steelblue1]/resume <id>[/]", "Resume a session by ID");
    table.AddRow ("[steelblue1]/history[/]", "Show current session info");
    table.AddRow ("[steelblue1]/help[/]", "Show this help");
    table.AddRow ("[steelblue1]/exit[/]", "End the session and quit");

    // Admin commands — styled yellow to emphasise that they affect the server
    // and all connected clients, not just the current session.
    table.AddRow ("[yellow]/admin:shutdown[/]", "[grey]Shut down the Raven.Core server (requires confirmation)[/]");
    table.AddRow ("[yellow]/admin:restart[/]", "[grey]Restart the Raven.Core server (requires confirmation)[/]");

    AnsiConsole.Write (table);
    AnsiConsole.WriteLine ();
  }

  public void WriteUserPrompt () =>
    // Markup (not MarkupLine) so the cursor stays on the same line as the prompt.
    AnsiConsole.Markup ("[steelblue1]>[/] ");

  // Render the streaming agent response using a two-phase approach:
  //
  // Phase 1 (streaming): consume the chunk stream silently while showing a
  // single-line spinner via AnsiConsole.Status. Status is designed exactly
  // for this — a fixed-height, single-line live region. It never touches the
  // scroll buffer above it, so there is no cursor-position corruption.
  //
  // Phase 2 (final): once the stream ends Status cleans up its own line, and
  // we write the complete accumulated text as formatted Markdown once into the
  // scroll buffer using AnsiConsole.Write.
  //
  // This replaces the previous plain-text-then-erase approach, which was
  // unreliable because accurate line counting requires knowing the true
  // terminal display width (affected by Unicode character widths, ConPTY
  // quirks, and window resize during streaming).
  public async Task RenderResponseStreamAsync (
      IAsyncEnumerable<string> chunks,
      CancellationToken cancellationToken = default)
  {
    AnsiConsole.MarkupLine ("[steelblue1_1]Raven:[/]");

    var accumulated = new System.Text.StringBuilder ();

    // Phase 1: accumulate chunks behind a spinner.
    await AnsiConsole.Status ()
        .Spinner (Spinner.Known.Dots)
        .SpinnerStyle (Style.Parse ("steelblue1_1 dim"))
        .StartAsync ("[dim]thinking…[/]", async ctx =>
        {
          await foreach (var chunk in chunks.WithCancellation (cancellationToken))
          {
            accumulated.Append (chunk);

            // Show the last ~60 chars of accumulated text as a live status
            // message so the user can see progress without scroll corruption.
            var preview = accumulated.ToString ();
            var start   = Math.Max (0, preview.Length - 60);
            var tail    = Markup.Escape (preview[start..].ReplaceLineEndings (" "));
            ctx.Status ($"[dim]{tail}[/]");
          }
        });

    // Phase 2: write the final formatted Markdown into the scroll buffer.
    if (accumulated.Length > 0)
      AnsiConsole.Write (MarkdownToSpectreRenderer.Render (accumulated.ToString ()));

    AnsiConsole.WriteLine ();
  }

  public void ShowError (string message)
  {
    // Markup.Escape ensures any brackets in the exception message are treated
    // as literal text rather than Spectre markup tags.
    AnsiConsole.MarkupLine ($"[red]Error:[/] {Markup.Escape (message)}");
    AnsiConsole.WriteLine ();
  }

  public void ShowWarning (string message)
  {
    AnsiConsole.MarkupLine ($"[yellow]Warning:[/] {Markup.Escape (message)}");
    AnsiConsole.WriteLine ();
  }

  public void ShowStaleSessionRecoveryPrompt ()
  {
    AnsiConsole.MarkupLine ("[grey]Press Enter to create a new session and continue.[/]");
  }

  public void ShowGoodbye (string? sessionId = null)
  {
    AnsiConsole.WriteLine ();
    if (!string.IsNullOrWhiteSpace (sessionId))
    {
      AnsiConsole.MarkupLine ($"[grey]Session [dim]{Markup.Escape (sessionId)}[/] is saved.[/]");
      AnsiConsole.MarkupLine ($"[grey]Resume it next time with: [/][steelblue1]--resume {Markup.Escape (sessionId)}[/]");
    }
    AnsiConsole.MarkupLine ("[grey]Goodbye.[/]");
  }

  public void ShowSessionInfo (SessionInfoResponse info)
  {
    var table = new Table()
            .BorderStyle(Style.Parse("grey"))
            .AddColumn(new TableColumn("[grey]Property[/]").NoWrap())
            .AddColumn(new TableColumn("[grey]Value[/]"));

    table.AddRow ("Session ID", $"[dim]{Markup.Escape (info.SessionId)}[/]");
    table.AddRow ("Started", $"[dim]{info.CreatedAt.ToLocalTime ():yyyy-MM-dd HH:mm:ss}[/]");
    table.AddRow ("Last activity", info.LastActivityAt.HasValue
        ? $"[dim]{info.LastActivityAt.Value.ToLocalTime ():yyyy-MM-dd HH:mm:ss}[/]"
        : "[dim]—[/]");

    AnsiConsole.Write (table);
    AnsiConsole.WriteLine ();
  }

  public void ShowNewSession (string oldSessionId, string newSessionId)
  {
    AnsiConsole.MarkupLine ($"[grey]Previous session [dim]{Markup.Escape (oldSessionId)}[/] closed.[/]");
    AnsiConsole.MarkupLine ($"[grey]New session:[/] [dim]{Markup.Escape (newSessionId)}[/]");
    AnsiConsole.WriteLine ();
  }

  public void ShowSessionList (IReadOnlyList<SessionSummary> sessions)
  {
    if (sessions.Count == 0)
    {
      AnsiConsole.MarkupLine ("[grey]No resumable sessions found.[/]");
      AnsiConsole.WriteLine ();
      return;
    }

    var table = new Table()
            .BorderStyle(Style.Parse("grey"))
            .AddColumn(new TableColumn("[grey]#[/]").NoWrap())
            .AddColumn(new TableColumn("[steelblue1]Session ID[/]").NoWrap())
            .AddColumn(new TableColumn("[grey]Started[/]").NoWrap())
            .AddColumn(new TableColumn("[grey]Last active[/]").NoWrap());

    for (int i = 0; i < sessions.Count; i++)
    {
      var s = sessions[i];
      table.AddRow (
          $"[grey]{i + 1}[/]",
          $"[dim]{Markup.Escape (s.SessionId)}[/]",
          $"[dim]{s.CreatedAt.ToLocalTime ():yyyy-MM-dd HH:mm}[/]",
          s.LastActivityAt.HasValue
              ? $"[dim]{s.LastActivityAt.Value.ToLocalTime ():yyyy-MM-dd HH:mm}[/]"
              : "[dim]—[/]");
    }

    AnsiConsole.Write (table);
    AnsiConsole.WriteLine ();
    AnsiConsole.MarkupLine ("[grey]Use [/][steelblue1]/resume <id>[/][grey] to switch to a session.[/]");
    AnsiConsole.WriteLine ();
  }

  public void ShowAdminCommandConfirmationPrompt (bool isRestart)
  {
    var action = isRestart ? "restart" : "shut down";
    AnsiConsole.MarkupLine ($"[yellow]This will {action} the Raven.Core server and disconnect all connected clients.[/]");
    AnsiConsole.Markup ("[grey]Type [/][steelblue1]yes[/][grey] to confirm, or press Enter to cancel: [/]");
  }

  public void ShowAdminCommandAccepted (bool isRestart)
  {
    AnsiConsole.WriteLine ();
    var action = isRestart ? "restarting" : "shutting down";
    AnsiConsole.MarkupLine ($"[yellow]Server is {action}. Goodbye.[/]");
    AnsiConsole.WriteLine ();
  }
}