namespace PSTextMate.Utilities;

/// <summary>
/// Forwards output to PSCmdlet streams. Pass this to internal classes
/// so they can write to verbose/debug/warning/error streams.
/// </summary>
/// <example>
/// <code>
/// // In cmdlet:
/// var streams = new Streams(this);
/// var processor = new DataProcessor(streams);
///
/// // In internal class:
/// public class DataProcessor {
///     private readonly Streams _ps;
///     public DataProcessor(Streams ps) => _ps = ps;
///     public void Process() => _ps.WriteVerbose("Processing...");
/// }
/// </code>
/// </example>
internal readonly struct Streams {
    private readonly PSCmdlet? _cmdlet;

    /// <summary>
    /// Creates a Streams wrapper for the given cmdlet.
    /// </summary>
    public Streams(PSCmdlet cmdlet) {
        _cmdlet = cmdlet;
    }

    /// <summary>
    /// Creates a null/no-op streams instance (for testing or when no cmdlet available).
    /// </summary>
    public static Streams Null => default;

    /// <summary>
    /// Returns true if this instance is connected to a cmdlet.
    /// </summary>
    public bool IsConnected => _cmdlet is not null;

    public void WriteVerbose(string message) => _cmdlet?.WriteVerbose(message);
    public void WriteDebug(string message) => _cmdlet?.WriteDebug(message);
    public void WriteWarning(string message) => _cmdlet?.WriteWarning(message);

    public void WriteError(ErrorRecord errorRecord) => _cmdlet?.WriteError(errorRecord);

    public void WriteError(Exception exception, string errorId, ErrorCategory errorCategory = ErrorCategory.NotSpecified, object? targetObject = null) =>
        _cmdlet?.WriteError(new ErrorRecord(exception, errorId, errorCategory, targetObject));

    public void WriteError(string message, ErrorCategory errorCategory = ErrorCategory.NotSpecified, object? targetObject = null) =>
        _cmdlet?.WriteError(new ErrorRecord(new InvalidOperationException(message), "TextMateError", errorCategory, targetObject));

    public void WriteInformation(string message, string[]? tags = null, string? source = null) =>
        _cmdlet?.WriteInformation(new InformationRecord(message, source), tags ?? []);
    public void WriteInformation(object MessageData, string[]? tags = null, string? source = null) =>
        _cmdlet?.WriteInformation(new InformationRecord(MessageData, source), tags ?? []);

    public void WriteObject(object? obj) => _cmdlet?.WriteObject(obj);
    public void WriteObject(object? obj, bool enumerateCollection) => _cmdlet?.WriteObject(obj, enumerateCollection);

    /// <summary>
    /// Writes progress information.
    /// </summary>
    public void WriteProgress(ProgressRecord progress) => _cmdlet?.WriteProgress(progress);

    /// <summary>
    /// Writes progress with simplified parameters.
    /// </summary>
    public void WriteProgress(string activity, string status, int percentComplete, int activityId = 0) =>
        _cmdlet?.WriteProgress(new ProgressRecord(activityId, activity, status) { PercentComplete = percentComplete });

    /// <summary>
    /// Completes a progress bar.
    /// </summary>
    public void CompleteProgress(int activityId = 0) =>
        _cmdlet?.WriteProgress(new ProgressRecord(activityId, "Complete", "Done") { RecordType = ProgressRecordType.Completed });
}
