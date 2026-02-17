#:package Markdig.Signed@0.38.0
using Markdig;
using Markdig.Renderers.Roundtrip;
using Markdig.Syntax;
// dotnet run ./diagnostic-trivia.cs ../tests/test-markdown.md
static int CountLines(string content) {
    if (string.IsNullOrEmpty(content)) {
        return 0;
    }

    int count = 0;
    using var reader = new StringReader(content.Replace("\r\n", "\n").Replace('\r', '\n'));
    while (reader.ReadLine() is not null) {
        count++;
    }

    return count;
}

string? firstArg = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.OrdinalIgnoreCase));
bool analyzeAll = args.Any(a => string.Equals(a, "--all", StringComparison.OrdinalIgnoreCase));
IEnumerable<string> targets = [];

if (analyzeAll) {
    string testDir = Path.Combine("..", "tests");
    if (!Directory.Exists(testDir)) {
        testDir = "tests";
    }

    if (!Directory.Exists(testDir)) {
        Console.Error.WriteLine($"Error: Could not find tests directory at {testDir}");
        return;
    }

    targets = Directory.GetFiles(testDir, "*.md", SearchOption.AllDirectories);
}
else if (!string.IsNullOrEmpty(firstArg)) {
    targets = [firstArg];
}
else {
    string defaultPath = Path.Combine("..", "tests", "test-markdown.md");
    if (!File.Exists(defaultPath)) {
        defaultPath = "tests/test-markdown.md";
    }
    targets = [defaultPath];
}

int totalSourceLines = 0;
int totalRoundtripLines = 0;
int processedFiles = 0;

foreach (string path in targets) {
    if (!File.Exists(path)) {
        Console.Error.WriteLine($"Error: Could not find markdown file at {path}");
        continue;
    }

    string markdown = File.ReadAllText(path);
    int sourceLines = CountLines(markdown);

    MarkdownDocument document = Markdown.Parse(markdown, trackTrivia: true);
    using var writer = new StringWriter();
    var roundtrip = new RoundtripRenderer(writer);
    roundtrip.Write(document);
    int roundtripLines = CountLines(writer.ToString());

    totalSourceLines += sourceLines;
    totalRoundtripLines += roundtripLines;
    processedFiles++;

    Console.WriteLine($"Analyzing: {Path.GetFullPath(path)}");
    Console.WriteLine($"Source line count: {sourceLines}");
    Console.WriteLine($"Roundtrip line count: {roundtripLines}");
    Console.WriteLine($"Delta: {roundtripLines - sourceLines}\n");

    Console.WriteLine("=== Complete Trivia Analysis (LinesBefore, LinesAfter, TriviaBefore, TriviaAfter) ===\n");

    for (int i = 0; i < document.Count; i++) {
        Block block = document[i];
        Console.WriteLine($"[{i}] {block.GetType().Name,-20} Line {block.Line,3}");

        if (block.LinesBefore != null && block.LinesBefore.Count > 0) {
            Console.WriteLine($"    LinesBefore.Count: {block.LinesBefore.Count}");
        }

        if (block.LinesAfter != null && block.LinesAfter.Count > 0) {
            Console.WriteLine($"    LinesAfter.Count: {block.LinesAfter.Count}");
        }
    }
}

if (processedFiles > 0) {
    Console.WriteLine("=== Summary ===");
    Console.WriteLine($"Files analyzed: {processedFiles}");
    Console.WriteLine($"Total source lines: {totalSourceLines}");
    Console.WriteLine($"Total roundtrip lines: {totalRoundtripLines}");
    Console.WriteLine($"Total delta: {totalRoundtripLines - totalSourceLines}");
}
