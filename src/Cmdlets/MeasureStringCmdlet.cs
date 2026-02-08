using System.Management.Automation;
using System.Runtime.InteropServices;
using PSTextMate.Helpers;

namespace PSTextMate.Commands;

/// <summary>
/// Cmdlet for measuring grapheme width and cursor movement for a string.
/// </summary>
[Cmdlet(VerbsDiagnostic.Measure, "String", DefaultParameterSetName = "Default")]
[OutputType(typeof(GraphemeMeasurement), typeof(bool), typeof(int))]
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

    [Parameter(
        ParameterSetName = "Wide"
    )]
    public SwitchParameter IsWide { get; set; }

    [Parameter(
        ParameterSetName = "Visible"
    )]
    public SwitchParameter VisibleLength { get; set; }
    protected override void ProcessRecord() {
        if (InputString is null) {
            return;
        }
        GraphemeMeasurement measurement = Grapheme.Measure(InputString, !IgnoreVT.IsPresent);
        switch (ParameterSetName) {
            case "Wide": {
                    WriteObject(measurement.HasWideCharacters);
                    break;
                }
            case "Visible": {
                    WriteObject(measurement.Cells);
                    break;
                }
            default: {
                    WriteObject(measurement);
                    break;
                }
        }
    }
}
