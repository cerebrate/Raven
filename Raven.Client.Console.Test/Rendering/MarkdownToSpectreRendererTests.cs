using ArkaneSystems.Raven.Client.Console.Rendering;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;

namespace ArkaneSystems.Raven.Client.Console.Test.Rendering;

// Tests for MarkdownToSpectreRenderer, which converts a Markdown string into a
// Spectre.Console IRenderable for display inside a Live region.
//
// Assertion strategy:
//   - Type checks (Markup / Rows) verify structural decisions independently of terminal output.
//   - Text-content checks use TestConsole, which renders without ANSI codes, giving plain text.
//   - "Markdown markers consumed" checks (DoesNotContain "**" etc.) confirm that Markdig parsed
//     the syntax rather than passing it through verbatim.
//   - "Spectre markup not leaked" checks (DoesNotContain "[bold]" etc.) confirm that style tags
//     were processed by Spectre rather than escaped and printed literally.
//   - Escape tests are the most critical: a missing Markup.Escape call causes Spectre to throw
//     when prose or code contains "[" characters that look like markup tags.
public sealed class MarkdownToSpectreRendererTests
{
  // ── Helpers ─────────────────────────────────────────────────────────────────

  private static string RenderToString (IRenderable renderable)
  {
    var console = new TestConsole ();
    console.Write (renderable);
    return console.Output;
  }

  // ── Empty / whitespace ──────────────────────────────────────────────────────

  [Theory]
  [InlineData ("")]
  [InlineData ("   ")]
  [InlineData ("\n\n")]
  public void WhenInputIsEmptyOrWhitespace_ReturnsMarkup (string input)
  {
    var result = MarkdownToSpectreRenderer.Render (input);

    Assert.IsType<Markup> (result);
  }

  // Regression: empty/whitespace input must never return Markup("") because
  // Spectre's SegmentShape.Calculate calls lines.Max() which throws
  // "Sequence contains no elements" on an empty segment list.
  [Theory]
  [InlineData ("")]
  [InlineData ("   ")]
  [InlineData ("\n\n")]
  public void WhenInputIsEmptyOrWhitespace_PlaceholderIsRenderable (string input)
  {
    // If this throws, the Live region in SpectreConsoleRenderer would crash.
    var output = RenderToString (MarkdownToSpectreRenderer.Render (input));

    Assert.NotEmpty (output.Trim ());
  }

  // ── Non-empty content always returns Rows ───────────────────────────────────

  [Fact]
  public void WhenInputHasContent_ReturnsRows ()
  {
    var result = MarkdownToSpectreRenderer.Render ("Hello world.");

    Assert.IsType<Rows> (result);
  }

  // ── Heading levels ──────────────────────────────────────────────────────────

  [Theory]
  [InlineData ("# Heading Text")]
  [InlineData ("## Heading Text")]
  [InlineData ("### Heading Text")]
  [InlineData ("#### Heading Text")]
  public void WhenHeadingAtAnyLevel_RenderedOutputContainsHeadingText (string markdown)
  {
    var output = RenderToString (MarkdownToSpectreRenderer.Render (markdown));

    Assert.Contains ("Heading Text", output);
  }

  [Theory]
  [InlineData ("# Heading Text")]
  [InlineData ("## Heading Text")]
  [InlineData ("### Heading Text")]
  [InlineData ("#### Heading Text")]
  public void WhenHeadingAtAnyLevel_HashPrefixIsConsumed (string markdown)
  {
    var output = RenderToString (MarkdownToSpectreRenderer.Render (markdown));

    Assert.DoesNotContain ("#", output);
  }

  // ── Inline emphasis ─────────────────────────────────────────────────────────

  [Theory]
  [InlineData ("**bold text**",     "bold text")]
  [InlineData ("*italic text*",     "italic text")]
  [InlineData ("***bold italic***", "bold italic")]
  public void WhenInlineEmphasis_TextIsPresentInOutput (string markdown, string expectedText)
  {
    var output = RenderToString (MarkdownToSpectreRenderer.Render (markdown));

    Assert.Contains (expectedText, output);
  }

  [Theory]
  [InlineData ("**bold**")]
  [InlineData ("*italic*")]
  [InlineData ("***both***")]
  public void WhenInlineEmphasis_MarkdownDelimitersAreConsumed (string markdown)
  {
    var output = RenderToString (MarkdownToSpectreRenderer.Render (markdown));

    Assert.DoesNotContain ("*", output);
  }

  [Theory]
  [InlineData ("**bold**",     "[bold]")]
  [InlineData ("*italic*",     "[italic]")]
  [InlineData ("***both***",   "[bold italic]")]
  public void WhenInlineEmphasis_SpectreMarkupTagIsNotLeaked (string markdown, string tag)
  {
    // If the tag appears literally in the output, the Markup string was incorrectly
    // escaped before being passed to Spectre — the style was never applied.
    var output = RenderToString (MarkdownToSpectreRenderer.Render (markdown));

    Assert.DoesNotContain (tag, output);
  }

  // ── Inline code ─────────────────────────────────────────────────────────────

  [Fact]
  public void WhenInlineCode_TextIsPresentInOutput ()
  {
    var output = RenderToString (MarkdownToSpectreRenderer.Render ("Use `foo()` here."));

    Assert.Contains ("foo()", output);
  }

  [Fact]
  public void WhenInlineCode_BackticksAreConsumed ()
  {
    var output = RenderToString (MarkdownToSpectreRenderer.Render ("Use `foo()` here."));

    Assert.DoesNotContain ("`", output);
  }

  // ── Escaping (most critical) ────────────────────────────────────────────────

  [Fact]
  public void WhenProseContainsSquareBrackets_RendersAsLiteralText ()
  {
    // Without Markup.Escape on literal text, Spectre would try to interpret
    // "[the docs]" as a markup colour/style tag and throw InvalidOperationException.
    var output = RenderToString (
        MarkdownToSpectreRenderer.Render ("Check [the docs] for details."));

    Assert.Contains ("[the docs]", output);
  }

  [Fact]
  public void WhenCodeBlockContainsSquareBrackets_RendersAsLiteralText ()
  {
    // Code content is passed to the Panel's inner Markup — without Markup.Escape
    // Spectre would throw on any "[" character in the code.
    var output = RenderToString (
        MarkdownToSpectreRenderer.Render ("```\nvar x = dict[key];\n```"));

    Assert.Contains ("dict[key]", output);
  }

  // ── Fenced code block ───────────────────────────────────────────────────────

  [Fact]
  public void WhenFencedCodeBlock_OutputContainsCodeContent ()
  {
    var output = RenderToString (
        MarkdownToSpectreRenderer.Render ("```csharp\nint x = 1;\n```"));

    Assert.Contains ("int x = 1;", output);
  }

  // ── Thematic break ──────────────────────────────────────────────────────────

  [Fact]
  public void WhenThematicBreak_RendersWithoutThrowingAndProducesOutput ()
  {
    var output = RenderToString (MarkdownToSpectreRenderer.Render ("---"));

    Assert.NotEmpty (output.Trim ());
  }

  // ── Bullet list ─────────────────────────────────────────────────────────────

  [Fact]
  public void WhenBulletList_OutputContainsBulletCharacterAndItemText ()
  {
    var output = RenderToString (
        MarkdownToSpectreRenderer.Render ("- alpha\n- beta\n- gamma"));

    Assert.Contains ("•", output);
    Assert.Contains ("alpha", output);
    Assert.Contains ("beta", output);
  }

  // ── Ordered list ────────────────────────────────────────────────────────────

  [Fact]
  public void WhenOrderedList_OutputContainsNumberedPrefixesAndItemText ()
  {
    var output = RenderToString (
        MarkdownToSpectreRenderer.Render ("1. first\n2. second"));

    Assert.Contains ("1.", output);
    Assert.Contains ("2.", output);
    Assert.Contains ("first", output);
    Assert.Contains ("second", output);
  }

  // ── Nested list ─────────────────────────────────────────────────────────────

  [Fact]
  public void WhenNestedBulletList_ChildItemHasMoreLeadingSpacesThanParent ()
  {
    var output = RenderToString (
        MarkdownToSpectreRenderer.Render ("- parent\n  - child"));

    var lines     = output.Split ('\n');
    var parentLine = Array.Find (lines, l => l.Contains ("parent"));
    var childLine  = Array.Find (lines, l => l.Contains ("child"));

    Assert.NotNull (parentLine);
    Assert.NotNull (childLine);

    var parentIndent = parentLine.Length - parentLine.TrimStart ().Length;
    var childIndent  = childLine.Length  - childLine.TrimStart ().Length;

    Assert.True (childIndent > parentIndent,
        $"Expected child indent ({childIndent}) to exceed parent indent ({parentIndent}).");
  }

  // ── Blockquote ──────────────────────────────────────────────────────────────

  [Fact]
  public void WhenBlockquote_OutputContainsPipePrefixAndQuoteText ()
  {
    var output = RenderToString (
        MarkdownToSpectreRenderer.Render ("> This is a quote."));

    Assert.Contains ("│", output);
    Assert.Contains ("This is a quote.", output);
  }

  // ── Partial / mid-stream Markdown ───────────────────────────────────────────

  [Fact]
  public void WhenPartialBoldMarker_RendersGracefullyWithoutThrowing ()
  {
    // Simulates the state mid-stream where a bold delimiter has been received
    // but the closing ** has not yet arrived. Markdig treats the unclosed marker
    // as literal text, so the content should still be present in the output.
    var output = RenderToString (
        MarkdownToSpectreRenderer.Render ("Hello **world"));

    Assert.Contains ("world", output);
  }

  // ── Rich Markdown (regression for "Sequence contains no elements") ───────────

  [Fact]
  public void WhenRichMarkdownWithAllCommonFeatures_RendersWithoutThrowing ()
  {
    const string markdown = """
      # Heading 1
      ## Heading 2

      Good morning! Here is a response using **all common Markdown features**.

      ## Emphasis

      Use *italic* and **bold** and ***bold italic***.

      ## Code

      Inline `code` looks like this.

      ## Lists

      - Item one
      - Item two
        - Nested item

      1. First
      2. Second

      ## Blockquote

      > This is a blockquote.

      ## Code Block

      ```csharp
      int x = 1;
      ```

      ## Thematic Break

      ---

      Visit [Spectre.Console](https://spectreconsole.net) for more info.
      """;

    var output = RenderToString (MarkdownToSpectreRenderer.Render (markdown));

    Assert.Contains ("Heading 1", output);
    Assert.Contains ("bold", output);
  }

  [Fact]
  public void WhenMarkdownContainsLink_RendersLinkTextWithoutThrowing ()
  {
    var output = RenderToString (
        MarkdownToSpectreRenderer.Render ("Visit [Spectre.Console](https://spectreconsole.net) for info."));

    Assert.Contains ("Spectre.Console", output);
  }

  // ── Table (regression: UseAdvancedExtensions enables table parsing) ──────────

  [Fact]
  public void WhenMarkdownTable_RendersWithoutThrowingAndContainsCellText ()
  {
    var markdown = "| Column A | Column B |\n|----------|----------|\n| Cell 1   | Cell 2   |\n| Cell 3   | Cell 4   |";

    var output = RenderToString (MarkdownToSpectreRenderer.Render (markdown));

    Assert.Contains ("Column A", output);
    Assert.Contains ("Cell 1", output);
  }
}
