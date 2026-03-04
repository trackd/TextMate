using System.Collections.Concurrent;
using System.Text;

namespace PSTextMate.Utilities;

internal static class StringBuilderPool {
    private static readonly ConcurrentBag<StringBuilder> _bag = [];

    public static StringBuilder Rent() => _bag.TryTake(out StringBuilder? sb) ? sb : new StringBuilder();

    public static void Return(StringBuilder sb) {
        if (sb is null) return;
        sb.Clear();
        _bag.Add(sb);
    }
}
