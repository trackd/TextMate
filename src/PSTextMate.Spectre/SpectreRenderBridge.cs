using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PSTextMate.Utilities;

/// <summary>Renders native output through its own Spectre assembly for isolated PowerShell consumers.</summary>
public static class SpectreRenderBridge {
    public static string RenderToString(object renderableObject, bool escapeAnsi = false, int? width = null) {
        ArgumentNullException.ThrowIfNull(renderableObject);
        while (renderableObject.GetType().FullName == "System.Management.Automation.PSObject")
            renderableObject = renderableObject.GetType().GetProperty("BaseObject")!.GetValue(renderableObject)!;
        if (renderableObject is not IRenderable renderable)
            throw new ArgumentException("Expected a native Spectre renderable.", nameof(renderableObject));
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings {
            Out = new AnsiConsoleOutput(writer),
            Ansi = escapeAnsi ? AnsiSupport.No : AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Interactive = InteractionSupport.No
        });
        console.Profile.Capabilities.Links = !escapeAnsi;
        if (width is > 0)
            console.Profile.Width = width.Value;
        console.Write(renderable);
        return writer.ToString();
    }
}
