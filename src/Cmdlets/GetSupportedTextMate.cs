using System.Management.Automation;
using TextMateSharp.Grammars;

namespace PSTextMate.Commands;

/// <summary>
/// Cmdlet for retrieving all supported TextMate languages and their configurations.
/// Returns detailed information about available grammars and extensions.
/// </summary>
[OutputType(typeof(Language))]
[Cmdlet(VerbsCommon.Get, "SupportedTextMate")]
public sealed class GetSupportedTextMateCmdlet : PSCmdlet {
    /// <summary>
    /// Finalizes processing and outputs all supported languages.
    /// </summary>
    protected override void EndProcessing() => WriteObject(TextMateHelper.AvailableLanguages, enumerateCollection: true);
}
