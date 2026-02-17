using System.IO;
using System.Management.Automation;
using TextMateSharp.Grammars;

namespace PSTextMate.Commands;

/// <summary>
/// Cmdlet for testing TextMate support for languages, extensions, and files.
/// Provides validation functionality to check compatibility before processing.
/// </summary>
[OutputType(typeof(bool))]
[Cmdlet(VerbsDiagnostic.Test, "SupportedTextMate", DefaultParameterSetName = "FileSet")]
public sealed class TestSupportedTextMateCmdlet : PSCmdlet {
    /// <summary>
    /// File extension to test for support (e.g., '.ps1').
    /// </summary>
    [Parameter(
        ParameterSetName = "ExtensionSet",
        Mandatory = true
    )]
    [ValidateNotNullOrEmpty]
    public string? Extension { get; set; }

    /// <summary>
    /// Language ID to test for support (e.g., 'powershell').
    /// </summary>
    [Parameter(
        ParameterSetName = "LanguageSet",
        Mandatory = true
    )]
    [ValidateNotNullOrEmpty]
    public string? Language { get; set; }

    /// <summary>
    /// File path to test for support.
    /// </summary>
    [Parameter(
        ParameterSetName = "FileSet",
        Mandatory = true
    )]
    [ValidateNotNullOrEmpty]
    public string? File { get; set; }

    /// <summary>
    /// Finalizes processing and outputs support check results.
    /// </summary>
    protected override void EndProcessing() {
        switch (ParameterSetName) {
            case "FileSet":
                FileInfo filePath = new(GetUnresolvedProviderPathFromPSPath(File!));
                if (!filePath.Exists) {
                    var exception = new FileNotFoundException($"File not found: {filePath.FullName}", filePath.FullName);
                    WriteError(new ErrorRecord(exception, nameof(TestSupportedTextMateCmdlet), ErrorCategory.ObjectNotFound, filePath.FullName));
                    return;
                }
                WriteObject(TextMateExtensions.IsSupportedFile(filePath.FullName));
                break;
            case "ExtensionSet":
                WriteObject(TextMateExtensions.IsSupportedExtension(Extension!));
                break;
            case "LanguageSet":
                WriteObject(TextMateLanguages.IsSupportedLanguage(Language!));
                break;
            default:
                break;
        }
    }
}
