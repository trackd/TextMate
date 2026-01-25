string path = args.Length > 0 ? args[0] : "../tests/test-markdown.md";
string markdown = File.ReadAllText(path);

// dotnet run ./test-line-calc.cs ../tests/test-markdown.md

var pipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .UseTaskLists()
    .UsePipeTables()
    .UseAutoLinks()
    .EnableTrackTrivia()
    .Build();

var document = Markdown.Parse(markdown, pipeline);

Block? previousBlock = null;
int totalGap = 0;

foreach (Block block in document) {
    if (previousBlock is not null) {
        int previousEndLine = GetBlockEndLine(previousBlock, markdown);
        int gap = block.Line - previousEndLine - 1;
        totalGap += gap;
        Console.WriteLine($"{previousBlock.GetType().Name,-20} ends at {previousEndLine,3} -> {block.GetType().Name,-20} at {block.Line,3} = gap {gap}");
    }
    previousBlock = block;
}

Console.WriteLine($"\nTotal blank lines from gaps: {totalGap}");
Console.WriteLine($"Document blocks: {document.Count}");
Console.WriteLine($"Expected output lines: {document.Count + totalGap}");

int GetBlockEndLine(Block block, string md) {
    // For container blocks, recursively find the last child's end line
    if (block is ContainerBlock container && container.Count > 0) {
        return GetBlockEndLine(container[^1], md);
    }
    // For fenced code blocks: opening fence + content lines + closing fence
    if (block is FencedCodeBlock fenced && fenced.Lines.Count > 0) {
        return block.Line + fenced.Lines.Count + 1;
    }
    // Count newlines within the block's span to find the ending line
    int endPosition = Math.Min(block.Span.End, md.Length - 1);
    int newlineCount = 0;
    for (int i = block.Span.Start; i <= endPosition; i++) {
        if (md[i] == '\n') {
            newlineCount++;
        }
    }
    return block.Line + newlineCount;
}
