namespace PSTextMate.Core;

internal sealed partial class PagerFrame {
    private bool _graphicsPainted;

    /// <summary>Kitty placements live above the text, so redrawing rows does not remove them.</summary>
    internal bool HasKittyPlacements { get; private set; }

    private void AppendGraphics(PagerState state, bool supported) {
        _graphicsPainted = false;
        if (HasKittyPlacements) {
            _frame.Write(VT.DeleteKittyPlacements);
            HasKittyPlacements = false;
        }
        if (state.Help || !supported || state.ImageCount == 0)
            return;
        for (int index = 0; index < state.ImageCount; index++) {
            TextBounds bounds = state.GetImageBounds(index);
            int top = bounds.Row - state.Top;
            if (bounds.Column + bounds.Width > state.Width)
                continue;
            int first = Math.Max(0, top);
            int last = Math.Min(state.ContentRows, top + bounds.Height);
            if (last <= first)
                continue;
            for (int row = first; row < last; row++) {
                _frame.MoveTo(bounds.Column + 1, row + state.SearchRows + 1);
                _frame.Write(VT.Reset);
                for (int column = 0; column < bounds.Width; column++)
                    _frame.Write(' ');
            }
            // Reserve the visible footprint, but never substitute lower-quality cells or draw a fragment.
            _graphicsPainted = true;
            if (top < 0 || top + bounds.Height > state.ContentRows)
                continue;
            TerminalImage image = state.ReadImage(index);
            // Every protocol draws at the cursor; the next row or image positions the cursor absolutely again.
            _frame.MoveTo(bounds.Column + 1, top + state.SearchRows + 1);
            VT.WriteImage(_frame, image);
            _frame.Write(VT.Reset);
            HasKittyPlacements |= image.Protocol == TerminalImageProtocol.Kitty;
        }
    }
}
