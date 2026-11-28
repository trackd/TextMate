namespace PSTextMate.Core;

internal readonly record struct PagerLine(HighlightedDocument Document, HighlightedLine Line, int Offset);

internal interface IPagerSource {
    int LineCount { get; }
    int Version { get; }
    bool IsComplete { get; }
    int ImageCount { get; }
    TextBounds GetImageBounds(int index);
    TerminalImage ReadImage(int index);
    void SetWidth(int width);
    PagerLine ReadLine(int index);
    int LineAt(int offset);
}
