using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Security;

// Width tests must not resize an inherited real console, even from a child process.
public sealed class TextMateWidthHost : PSHost {
    private readonly WidthUI _ui = new();
    public void SetWidth(int width) => _ui.Raw.WindowSize = new Size(width, 40);
    public override PSHostUserInterface UI => _ui;
    public override string Name => "TextMate isolated width host";
    public override Version Version => new(1, 0);
    public override Guid InstanceId { get; } = Guid.NewGuid();
    public override CultureInfo CurrentCulture => CultureInfo.InvariantCulture;
    public override CultureInfo CurrentUICulture => CultureInfo.InvariantCulture;
    public override void EnterNestedPrompt() => throw new NotSupportedException();
    public override void ExitNestedPrompt() => throw new NotSupportedException();
    public override void NotifyBeginApplication() {

    }
    public override void NotifyEndApplication() {

    }
    public override void SetShouldExit(int exitCode) {

    }

    private sealed class WidthUI : PSHostUserInterface {
        internal WidthRawUI Raw { get; } = new();
        public override PSHostRawUserInterface RawUI => Raw;
        public override string ReadLine() => throw new NotSupportedException();
        public override SecureString ReadLineAsSecureString() => throw new NotSupportedException();
        public override void Write(string value) {

        }
        public override void Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value) {

        }
        public override void WriteLine(string value) {

        }
        public override void WriteErrorLine(string value) {

        }
        public override void WriteDebugLine(string message) {

        }
        public override void WriteVerboseLine(string message) {

        }
        public override void WriteWarningLine(string message) {

        }
        public override void WriteProgress(long sourceId, ProgressRecord record) {

        }
        public override Dictionary<string, PSObject> Prompt(string caption, string message, Collection<FieldDescription> descriptions) => throw new NotSupportedException();
        public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName) => throw new NotSupportedException();
        public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName, PSCredentialTypes allowedCredentialTypes, PSCredentialUIOptions options) => throw new NotSupportedException();
        public override int PromptForChoice(string caption, string message, Collection<ChoiceDescription> choices, int defaultChoice) => throw new NotSupportedException();
    }

    private sealed class WidthRawUI : PSHostRawUserInterface {
        public override ConsoleColor ForegroundColor { get; set; } = ConsoleColor.Gray;
        public override ConsoleColor BackgroundColor { get; set; } = ConsoleColor.Black;
        public override Coordinates CursorPosition { get; set; }
        public override Coordinates WindowPosition { get; set; }
        public override int CursorSize { get; set; } = 25;
        public override Size BufferSize { get; set; } = new(160, 1000);
        public override Size WindowSize { get; set; } = new(120, 40);
        public override Size MaxWindowSize => new(160, 40);
        public override Size MaxPhysicalWindowSize => new(160, 40);
        public override bool KeyAvailable => false;
        public override string WindowTitle { get; set; } = "Width test";
        public override void FlushInputBuffer() {

        }
        public override KeyInfo ReadKey(ReadKeyOptions options) => throw new NotSupportedException();
        public override BufferCell[,] GetBufferContents(Rectangle rectangle) => throw new NotSupportedException();
        public override void SetBufferContents(Coordinates origin, BufferCell[,] contents) => throw new NotSupportedException();
        public override void SetBufferContents(Rectangle rectangle, BufferCell fill) => throw new NotSupportedException();
        public override void ScrollBufferContents(Rectangle source, Coordinates destination, Rectangle clip, BufferCell fill) => throw new NotSupportedException();
    }
}
