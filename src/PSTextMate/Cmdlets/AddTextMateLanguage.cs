using Tint.Grammars;
using Tint.Loading;

namespace PSTextMate.Commands;

/// <summary>Registers a local TextMate grammar without adding assets to the module.</summary>
[Cmdlet(VerbsCommon.Add, "TextMateLanguage", SupportsShouldProcess = true)]
[OutputType(typeof(Language))]
public sealed class AddTextMateLanguageCmdlet : PSCmdlet {
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [Alias("FullName", "LiteralPath")]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    [Parameter]
    public string[] Extension { get; set; } = [];

    [Parameter]
    public string[] Alias { get; set; } = [];

    [Parameter]
    public SwitchParameter Force { get; set; }

    [Parameter]
    public SwitchParameter PassThru { get; set; }

    protected override void ProcessRecord() {
        try {
            ArgumentException.ThrowIfNullOrWhiteSpace(Name);
            if (Name.Equals("plaintext", StringComparison.OrdinalIgnoreCase) || Name.Equals("text", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The plaintext and text language names are reserved.", nameof(Name));
            string file = GetUnresolvedProviderPathFromPSPath(Path);
            string[] extensions = new string[Extension.Length];
            for (int i = 0; i < extensions.Length; i++) {
                ArgumentException.ThrowIfNullOrWhiteSpace(Extension[i]);
                extensions[i] = Extension[i].StartsWith('.') ? Extension[i] : "." + Extension[i];
                if (!Force.IsPresent && GrammarCatalog.Instance.ByExtension.ContainsKey(extensions[i]))
                    throw new ArgumentException($"Extension '{extensions[i]}' is already registered. Use -Force to replace it.", nameof(Extension));
            }
            LoadedGrammar source = GrammarLoader.Load(file, new GrammarInfo {
                LanguageId = Name,
                FileExtensions = extensions,
                Aliases = Alias.Length == 0 ? [Name] : Alias
            });
            source.GetGrammar();
            if (!ShouldProcess(Name, $"Register TextMate grammar from '{file}'"))
                return;
            HighlightingExtensions.RegisterGrammar(source, Force.IsPresent);
            if (PassThru.IsPresent)
                WriteObject(GrammarCatalog.Instance.Resolve(Name, false));
        }
        catch (Exception error) when (error is not PipelineStoppedException and not OperationCanceledException and not OutOfMemoryException) {
            WriteError(new ErrorRecord(error, "TextMateLanguageRegistrationFailed", ErrorCategory.InvalidData, Path));
        }
    }
}
