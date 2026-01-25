using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace PSTextMate.Tests.Integration;

public class RenderingIntegrationTests
{
    private static string RunRepScript()
    {
        var psi = new ProcessStartInfo()
        {
            FileName = "pwsh",
            Arguments = "-NoProfile -File .\\rep.ps1",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = System.IO.Path.GetFullPath(".")
        };

        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(30000);

        if (p.ExitCode != 0)
        {
            throw new Exception($"rep.ps1 failed: exit={p.ExitCode}\n{stderr}");
        }

        return stdout;
    }

    [Fact]
    public void RepScript_Includes_InlineCode_And_Links_And_ImageText()
    {
        string output = RunRepScript();

        output.Should().Contain("Inline code: Write-Host");
        output.Should().Contain("GitHub");
        output.Should().Contain("Blue styled link");
        output.Should().Contain("xkcd git");
    }
}
