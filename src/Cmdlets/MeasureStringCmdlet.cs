using System.Management.Automation;
using PSTextMate.Helpers;

namespace PSTextMate.Commands;

/// <summary>
/// Cmdlet for measuring grapheme width and cursor movement for a string.
/// </summary>
[Cmdlet(VerbsDiagnostic.Measure, "String")]
[OutputType(typeof(Grapheme.GraphemeMeasurement), typeof(bool), typeof(int))]
public sealed class MeasureStringCmdlet : PSCmdlet {
    /// <summary>
    /// The input string to measure.
    /// </summary>
    [Parameter(
        Mandatory = true,
        Position = 0,
        ValueFromPipeline = true,
        ValueFromPipelineByPropertyName = true
    )]
    [AllowEmptyString]
    public string? InputString { get; set; }

    [Parameter]
    public SwitchParameter IgnoreVT { get; set; }

    [Parameter]
    public SwitchParameter IsWide { get; set; }

    [Parameter]
    public SwitchParameter VisibleLength { get; set; }
    protected override void ProcessRecord() {
        if (InputString is null) {
            return;
        }
        Grapheme.GraphemeMeasurement measurement = Grapheme.Measure(InputString, !IgnoreVT.IsPresent);
        if (IsWide) {
            WriteObject(measurement.HasWideCharacters);
            return;
        }
        if (VisibleLength) {
            WriteObject(measurement.Cells);
            return;
        }
        WriteObject(measurement);
    }
}
