using PokeTokenBar.Core.Io;

namespace PokeTokenBar.Core.Tests;

public sealed class FileGateTests
{
    [Fact]
    public void HoldsTheGateForTheFirstCaller()
    {
        using var directory = new TempGateDirectory();

        using var gate = FileGate.Acquire(directory.File);

        Assert.True(gate.Held);
    }

    [Fact]
    public void ASecondCallerIsBlockedWhileTheFirstHolds()
    {
        // The point of the gate: every window runs its own sidecar against one state file, and
        // two that interleave would both apply the same token delta.
        using var directory = new TempGateDirectory();
        using var first = FileGate.Acquire(directory.File);

        using var second = FileGate.Acquire(directory.File, TimeSpan.FromMilliseconds(150));

        Assert.True(first.Held);
        Assert.False(second.Held);
    }

    [Fact]
    public void ReleasingLetsTheNextCallerIn()
    {
        using var directory = new TempGateDirectory();

        using (var first = FileGate.Acquire(directory.File))
        {
            Assert.True(first.Held);
        }

        using var second = FileGate.Acquire(directory.File, TimeSpan.FromMilliseconds(500));

        Assert.True(second.Held);
    }

    [Fact]
    public void TimingOutYieldsAnUnheldGateRatherThanThrowing()
    {
        // A companion that advances slightly wrong beats a refresh that fails, and beats
        // blocking forever on a lock left by a killed process.
        using var directory = new TempGateDirectory();
        using var first = FileGate.Acquire(directory.File);

        var second = FileGate.Acquire(directory.File, TimeSpan.FromMilliseconds(50));

        Assert.False(second.Held);
        second.Dispose();
    }

    [Fact]
    public void OnlyOneOfManyContendersHoldsAtOnce()
    {
        using var directory = new TempGateDirectory();
        var held = 0;
        var maxConcurrent = 0;
        var sync = new object();

        Parallel.For(0, 8, _ =>
        {
            using var gate = FileGate.Acquire(directory.File, TimeSpan.FromSeconds(3));
            if (!gate.Held)
            {
                return;
            }

            lock (sync)
            {
                held++;
                maxConcurrent = Math.Max(maxConcurrent, held);
            }

            Thread.Sleep(15);

            lock (sync)
            {
                held--;
            }
        });

        Assert.Equal(1, maxConcurrent);
    }

    [Fact]
    public void DoesNotLeaveALockFileBehind()
    {
        using var directory = new TempGateDirectory();

        using (FileGate.Acquire(directory.File))
        {
        }

        Assert.False(File.Exists(directory.File + ".lock"));
    }

    private sealed class TempGateDirectory : IDisposable
    {
        public TempGateDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "ptb-gate-" + Guid.NewGuid().ToString("N")[..10]);
            Directory.CreateDirectory(Root);
            File = Path.Combine(Root, "state.json");
        }

        public string Root { get; }

        public string File { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
