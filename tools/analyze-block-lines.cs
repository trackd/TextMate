#:package Markdig.Signed@0.38.0
using Markdig;
using Markdig.Syntax;
// dotnet run ./analyze-block-lines.cs ../tests/test-markdown.md
string path = args.Length > 0 ? args[0] : "tests/test-markdown.md";
string markdown = File.ReadAllText(path);

MarkdownPipeline pipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .UseTaskLists()
    .UsePipeTables()
    .UseAutoLinks()
    .EnableTrackTrivia()
    .Build();

MarkdownDocument document = Markdown.Parse(markdown, pipeline);

Console.WriteLine($"Analyzing blocks in: {path}\n");

Block? previousBlock = null;
int totalGapLines = 0;

for (int i = 0; i < document.Count; i++) {
    Block block = document[i];
    int endLine = GetBlockEndLine(block, markdown);

    if (previousBlock is not null) {
        int prevEndLine = GetBlockEndLine(previousBlock, markdown);
        int gap = block.Line - prevEndLine - 1;
        totalGapLines += Math.Max(0, gap);
        Console.WriteLine($"  Gap: {prevEndLine} -> {block.Line} = {gap} blank lines");
    }

    Console.WriteLine($"[{i,2}] {block.GetType().Name,-20} Line {block.Line,3} -> {endLine,3}  Span: {block.Span.Start}-{block.Span.End}");
    previousBlock = block;
}

int sourceLines = markdown.Split('\n').Length;
Console.WriteLine($"\nSource lines: {sourceLines}");
Console.WriteLine($"Document blocks: {document.Count}");
Console.WriteLine($"Total gap lines: {totalGapLines}");
Console.WriteLine($"Expected rendered lines (blocks + gaps): {document.Count + totalGapLines}");

static int GetBlockEndLine(Block block, string md) {
    // For container blocks, recursively find the last child's end line
    if (block is ContainerBlock container && container.Count > 0) {
        return GetBlockEndLine(container[^1], md);
    }
    // For fenced code blocks: opening fence + content lines + closing fence
    if (block is FencedCodeBlock fenced && fenced.Lines.Count > 0) {
        return block.Line + fenced.Lines.Count + 1;
    }
    // Count newlines within the block's span (excluding the final newline which separates blocks)
    // The span typically includes the trailing newline, so we stop before Span.End
    int endPosition = Math.Min(block.Span.End - 1, md.Length - 1);
    int newlineCount = 0;
    for (int i = block.Span.Start; i <= endPosition; i++) {
        if (md[i] == '\n') {
            newlineCount++;
        }
    }
    return block.Line + newlineCount;
}
