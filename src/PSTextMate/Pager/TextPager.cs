namespace PSTextMate.Core;

/// <summary>Built-in document pager. Native output adapters do not own terminal sessions.</summary>
public static class TextPager {
    public static void ShowText(string text) => Show(VT.Parse(text));

    public static void Show(HighlightedDocument document, RendererOptions options = default, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(document);
        if (Console.IsInputRedirected || Console.IsOutputRedirected) {
            DocumentPresentation.Prepare(document, options).WriteTo(Console.Out, ansi: false);
            return;
        }
        TerminalGraphics graphics = document.Images.IsEmpty && document.Language != "markdown"
            ? default : DocumentPresentation.GetTerminalGraphics();
        TerminalPager.Run(new PagerState(new TextMateView(document, options)), new PagerTerminal(graphics), cancellationToken);
    }
}
