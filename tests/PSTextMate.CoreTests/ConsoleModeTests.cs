using PSTextMate.Core;
using Xunit;

namespace PSTextMate.CoreTests;

public sealed class ConsoleModeTests {
    [Fact]
    public void Lease_RestoresEveryEntryBitAndUsesKeyRecordInputInsteadOfRawVt() {
        var api = new FakeModes();
        uint input = api.Input;
        uint output = api.Output;
        using (var scope = new ConsoleModeLease(api)) {
            Assert.Equal(0u, api.Input & 0x0247u);
            Assert.NotEqual(0u, api.Input & 0x0080u);
            Assert.Equal(output | 5u, api.Output);
        }
        Assert.Equal(input, api.Input);
        Assert.Equal(output, api.Output);
    }

    [Fact]
    public void Lease_RestoresModesWhenOwnedOperationThrows() {
        var api = new FakeModes();
        Assert.Throws<InvalidOperationException>((Action)(() => {
            using var scope = new ConsoleModeLease(api);
            throw new InvalidOperationException("Simulated pager failure");
        }));
        Assert.Equal(0x03ffu, api.Input);
        Assert.Equal(0x0003u, api.Output);
    }

    [Fact]
    public void Lease_RollsBackPartialAcquisitionAndDisposeIsIdempotent() {
        var api = new FakeModes { FailOutputOnce = true };
        Assert.Throws<IOException>(() => new ConsoleModeLease(api));
        Assert.Equal(0x03ffu, api.Input);
        Assert.Equal(0x0003u, api.Output);
        var scope = new ConsoleModeLease(api);
        scope.Dispose();
        int calls = api.Sets;
        scope.Dispose();
        Assert.Equal(calls, api.Sets);
    }

    [Fact]
    public void Lease_AttemptsInputRestorationEvenWhenOutputRestorationFails() {
        var api = new FakeModes();
        var scope = new ConsoleModeLease(api);
        api.FailOutputOnce = true;
        Assert.Throws<IOException>(scope.Dispose);
        Assert.Equal(0x03ffu, api.Input);
        scope.Dispose();
        Assert.Equal(0x0003u, api.Output);
    }

    private sealed class FakeModes : IConsoleModeApi {
        internal uint Input = 0x03ffu;
        internal uint Output = 3;
        internal int Sets;
        internal bool FailOutputOnce;
        public int Error => 5;
        public nint Handle(int kind) => kind == -10 ? 1 : 2;
        public bool Get(nint handle, out uint mode) {
            mode = handle == 1 ? Input : Output;
            return true;
        }
        public bool Set(nint handle, uint mode) {
            Sets++;
            if (handle == 2 && FailOutputOnce) {
                FailOutputOnce = false;
                return false;
            }
            if (handle == 1)
                Input = mode;
            else
                Output = mode;
            return true;
        }
    }
}
