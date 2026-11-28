using Tint.Loading;

namespace PSTextMate.Commands;

/// <summary>Registers a local TextMate theme for future documents.</summary>
[Cmdlet(VerbsCommon.Add, "TextMateTheme", SupportsShouldProcess = true)]
[OutputType(typeof(string))]
public sealed class AddTextMateThemeCmdlet : PSCmdlet {
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [Alias("FullName", "LiteralPath")]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    [Parameter]
    public SwitchParameter Force { get; set; }

    [Parameter]
    public SwitchParameter PassThru { get; set; }

    protected override void ProcessRecord() {
        try {
            string file = GetUnresolvedProviderPathFromPSPath(Path);
            LoadedTheme source = ThemeLoader.Load(file);
            Tint.Theme.FromSource(source);
            if (!ShouldProcess(Name, $"Register TextMate theme from '{file}'"))
                return;
            HighlightingExtensions.RegisterTheme(Name, source, Force.IsPresent);
            if (PassThru.IsPresent)
                WriteObject(Name);
        }
        catch (Exception error) when (error is not PipelineStoppedException and not OperationCanceledException and not OutOfMemoryException) {
            WriteError(new ErrorRecord(error, "TextMateThemeRegistrationFailed", ErrorCategory.InvalidData, Path));
        }
    }
}
