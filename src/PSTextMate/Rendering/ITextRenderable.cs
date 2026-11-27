namespace PSTextMate.Core;

/// <summary>Resolves content at a terminal-cell width into an immutable, framework-independent presentation.</summary>
public interface ITextRenderable {
    HighlightedDocument Layout(int width);
}
