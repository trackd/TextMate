using System;
using System.IO;
using System.Management.Automation.Host;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace PSTextMate.Utilities;

/// <summary>
/// Provides an ALC-safe bridge for rendering Spectre objects to plain text.
/// </summary>
public static class SpectreRenderBridge {
    /// <summary>
    /// Renders a Spectre renderable object to a string.
    /// </summary>
    /// <param name="renderableObject">Object implementing <see cref="IRenderable"/>.</param>
    /// <param name="escapeAnsi">When true, strips ANSI sequences from the output.</param>
    /// <returns>The rendered string output.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="renderableObject"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="renderableObject"/> does not implement <see cref="IRenderable"/>.</exception>
    public static string RenderToString(object renderableObject, bool escapeAnsi = false) {
        ArgumentNullException.ThrowIfNull(renderableObject);

        if (renderableObject is not IRenderable renderable) {
            throw new ArgumentException(
                $"Object of type '{renderableObject.GetType().FullName}' does not implement {nameof(IRenderable)}.",
                nameof(renderableObject)
            );
        }

        using StringWriter writer = new();
        var output = new AnsiConsoleOutput(writer);
        var settings = new AnsiConsoleSettings { Out = output };
        IAnsiConsole console = AnsiConsole.Create(settings);
        console.Write(renderable);

        string rendered = writer.ToString();
        return escapeAnsi ? PSHostUserInterface.GetOutputString(rendered, false) : rendered;
    }
}
