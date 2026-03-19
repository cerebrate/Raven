using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text;

namespace ArkaneSystems.Raven.Client.Console.Rendering;

// Converts a Markdown string to a Spectre.Console IRenderable.
// Used by SpectreConsoleRenderer to display agent responses with live formatting
// as chunks stream in. Called on every chunk, so the full document is re-parsed
// and re-rendered each time — acceptable for typical LLM response sizes.
//
// Block mapping:
//   HeadingBlock       → Markup with [bold] / [underline] styles by level
//   ParagraphBlock     → Markup with inline emphasis/code formatting
//   FencedCodeBlock    → Panel containing escaped plain text (no markup parsing)
//   ListBlock          → Markup with bullet or numbered prefixes, nested indent
//   QuoteBlock         → Markup with italic grey styling
//   ThematicBreakBlock → Rule
//
// All blocks are collected into a Rows container for vertical stacking.
internal static class MarkdownToSpectreRenderer
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

    internal static IRenderable Render(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return new Markup("[dim]…[/]");

        var doc = Markdown.Parse(markdown, Pipeline);
        var blocks = new List<IRenderable>(doc.Count);

        foreach (var block in doc)
        {
            if (RenderBlock(block) is { } rendered)
                blocks.Add(rendered);
        }

        return blocks.Count == 0
            ? new Markup("[dim]…[/]")
            : new Rows(blocks);
    }

    private static IRenderable? RenderBlock(Block block) => block switch
    {
        HeadingBlock heading         => RenderHeading(heading),
        ParagraphBlock paragraph     => RenderParagraph(paragraph),
        FencedCodeBlock code         => RenderCodeBlock(code),
        CodeBlock code               => RenderCodeBlock(code),
        ListBlock list               => RenderList(list),
        QuoteBlock quote             => RenderQuote(quote),
        ThematicBreakBlock           => new Rule().RuleStyle("grey"),
        ContainerBlock container     => RenderContainer(container),
        _                            => null,
    };

    private static IRenderable? RenderHeading(HeadingBlock heading)
    {
        // Map heading levels to progressively less prominent Spectre styles.
        var (open, close) = heading.Level switch
        {
            1 => ("[bold underline steelblue1_1]", "[/]"),
            2 => ("[bold steelblue1]",             "[/]"),
            3 => ("[bold]",                         "[/]"),
            _ => ("[italic]",                       "[/]"),
        };

        var sb = new StringBuilder();
        if (heading.Inline is not null)
            AppendInlines(heading.Inline, sb);

        // Skip empty headings — a tag-pair with no content (e.g. [bold][/]) renders
        // to zero segments and crashes Spectre's SegmentShape.Calculate.
        var text = sb.ToString();
        return string.IsNullOrEmpty(text) ? null : new Markup(open + text + close);
    }

    private static IRenderable? RenderParagraph(ParagraphBlock paragraph)
    {
        var sb = new StringBuilder();
        if (paragraph.Inline is not null)
            AppendInlines(paragraph.Inline, sb);

        // Return null for empty paragraphs so they are filtered out in Render().
        // An empty Markup("") inside a Rows causes Spectre's SegmentShape.Calculate
        // to call lines.Max() on an empty list, throwing "Sequence contains no elements".
        var text = sb.ToString();
        return string.IsNullOrEmpty(text) ? null : new Markup(text);
    }

    private static IRenderable? RenderCodeBlock(LeafBlock code)
    {
        // Build the code content as plain escaped text.
        // Using a Panel visually separates the code block from surrounding prose
        // and avoids any risk of Spectre markup characters in code being interpreted.
        var sb = new StringBuilder();
        for (var i = 0; i < code.Lines.Count; i++)
        {
            var slice = code.Lines.Lines[i].Slice;
            if (slice.Text is not null)
                sb.AppendLine(Markup.Escape(slice.ToString()));
        }

        var content = sb.ToString().TrimEnd();

        // Skip empty code blocks — Panel(Markup("")) renders to zero segments.
        if (string.IsNullOrEmpty(content))
            return null;

        return new Panel(new Markup(content))
            .BorderStyle(Style.Parse("grey dim"))
            .Expand();
    }

    private static IRenderable? RenderList(ListBlock list)
    {
        var sb = new StringBuilder();
        AppendListItems(list, sb, depth: 0);
        var text = sb.ToString().TrimEnd();
        return string.IsNullOrEmpty(text) ? null : new Markup(text);
    }

    private static void AppendListItems(ListBlock list, StringBuilder sb, int depth)
    {
        var indent = new string(' ', depth * 2);
        var index  = list.IsOrdered && int.TryParse(list.OrderedStart, out var n) ? n : 1;

        foreach (var item in list.OfType<ListItemBlock>())
        {
            var bullet  = list.IsOrdered ? $"{index++}. " : "• ";
            var isFirst = true;

            foreach (var child in item)
            {
                switch (child)
                {
                    case ParagraphBlock para when para.Inline is not null:
                        // First paragraph sits on the bullet line; subsequent ones indent to align.
                        sb.Append(indent);
                        sb.Append(isFirst ? bullet : new string(' ', bullet.Length));
                        AppendInlines(para.Inline, sb);
                        sb.AppendLine();
                        isFirst = false;
                        break;

                    case ListBlock nested:
                        AppendListItems(nested, sb, depth + 1);
                        break;
                }
            }
        }
    }

    private static IRenderable? RenderQuote(QuoteBlock quote)
    {
        var sb = new StringBuilder();

        foreach (var child in quote)
        {
            if (child is ParagraphBlock para && para.Inline is not null)
            {
                sb.Append("│ ");
                AppendInlines(para.Inline, sb);
                sb.AppendLine();
            }
        }

        var inner = sb.ToString().TrimEnd();

        // Return null for empty blockquotes — Markup("[italic grey][/]") renders
        // to zero segments inside a Live region and crashes Spectre's renderer.
        return string.IsNullOrEmpty(inner)
            ? null
            : new Markup("[italic grey]" + inner + "[/]");
    }

    private static IRenderable? RenderContainer(ContainerBlock container)
    {
        var children = container
            .Select(RenderBlock)
            .OfType<IRenderable>()
            .ToList();

        return children.Count == 0 ? null : new Rows(children);
    }

    private static void AppendInlines(ContainerInline container, StringBuilder sb)
    {
        foreach (var inline in container)
            AppendInline(inline, sb);
    }

    private static void AppendInline(Inline inline, StringBuilder sb)
    {
        switch (inline)
        {
            case LiteralInline literal:
                sb.Append(Markup.Escape(literal.Content.ToString()));
                break;

            case EmphasisInline emphasis:
                // DelimiterCount: 1 = italic (*text*), 2 = bold (**text**), 3 = bold+italic (***text***)
                var tag = emphasis.DelimiterCount switch
                {
                    >= 3 => "bold italic",
                    2    => "bold",
                    _    => "italic",
                };
                sb.Append($"[{tag}]");
                foreach (var child in emphasis)
                    AppendInline(child, sb);
                sb.Append("[/]");
                break;

            case CodeInline code:
                // Inline code uses blue to visually distinguish it from prose.
                sb.Append($"[blue]{Markup.Escape(code.Content)}[/]");
                break;

            case LineBreakInline lineBreak:
                // Hard line breaks (two spaces + newline) force a new line;
                // soft breaks are rendered as a space to preserve flow.
                sb.Append(lineBreak.IsHard ? "\n" : " ");
                break;

            case ContainerInline container:
                AppendInlines(container, sb);
                break;
        }
    }
}
