// Adapted from Statusline (MIT), Copyright (c) 2025 Andree Renneus.
// Grapheme tables and rules derived from Microsoft Windows Terminal (MIT).
// See LICENSE.Unicode.txt for attribution and table provenance.
namespace PSTextMate.Core;

internal static partial class UnicodeGraphemes {
    // Retained from Windows Terminal / ref/Codepoint Grapheme mode:
    // ambiguous characters are narrow, and FE0F requests a two-column emoji cluster.
    private const int AmbiguousWidth = 1;
    internal struct GraphemeState {
        public int Beg;
        public int Len;
        public int Width;
        internal uint State;
        internal int Last;

        /// <summary>
        /// Initializes a new grapheme state starting at the given offset.
        /// </summary>
        /// <param name="beg">Starting UTF-16 offset.</param>
        public GraphemeState(int beg) {
            Beg = beg;
            Len = 0;
            Width = 0;
            State = 0;
            Last = 0;
        }
    }

    internal static bool Next(ref GraphemeState s, ReadOnlySpan<char> str) {
        int end = str.Length;
        int clusterBeg = s.Beg + s.Len;
        int width = s.Width;
        uint state = s.State;
        int lead = s.Last;

        int clusterEnd = clusterBeg;

        if (clusterEnd < end) {
            uint cp = 0;
            bool resume = state != 0;
            state = ~state;

            if (!resume) {
                clusterEnd = Utf16NextOrFFFD(str, clusterEnd, out cp);
                lead = UcdLookup(cp);
                width = 0;
                state = 0;
            }

            for (; ; )
{
                if (!resume) {
                    int w = UcdToCharacterWidth(lead);
                    if (w == 3) {
                        w = AmbiguousWidth;
                    }

                    if (cp == 0xFE0F) {
                        w = 2;
                    }

                    width += w;

                    if (clusterEnd >= end) {
                        break;
                    }
                }

                resume = false;


                if (clusterEnd >= end) {
                    break;
                }

                int clusterEndNext = Utf16NextOrFFFD(str, clusterEnd, out cp);
                int trail = UcdLookup(cp);

                state = UcdGraphemeJoins(state, lead, trail);
                if (UcdGraphemeDone(state)) {
                    state = ~0u;
                    lead = 0;
                    break;
                }

                clusterEnd = clusterEndNext;
                lead = trail;
            }

            state = ~state;
            if (width > 2) {
                width = 2;
            }

            s.Beg = clusterBeg;
            s.Len = clusterEnd - clusterBeg;
            s.Width = width;
            s.State = state;
            s.Last = lead;
        }

        return clusterEnd < end;
    }
}
