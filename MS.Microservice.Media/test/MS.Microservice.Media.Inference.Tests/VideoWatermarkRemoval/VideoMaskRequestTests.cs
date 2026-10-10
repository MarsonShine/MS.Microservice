using System.Collections;
using System.Text.Json;
using MS.Microservice.Media.Inference.VideoWatermarkRemoval;
using Xunit;

namespace MS.Microservice.Media.Inference.Tests.VideoWatermarkRemoval;

public sealed class VideoMaskRequestTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void RequestBudgets_RequirePositiveValues(int value)
    {
        Assert.Throws<ArgumentException>(() => new VideoRepairLimits { MaximumMaskKeyframes = value }.Validate());
        Assert.Throws<ArgumentException>(() => new VideoRepairLimits { MaximumRectanglesPerKeyframe = value }.Validate());
        Assert.Throws<ArgumentException>(() => new VideoRepairLimits { MaximumMaskRectangles = value }.Validate());
    }

    [Theory]
    [InlineData(3, 2, 3, 2, 6, true)]
    [InlineData(4, 2, 3, 2, 8, false)]
    [InlineData(3, 3, 3, 2, 9, false)]
    [InlineData(3, 2, 3, 2, 5, false)]
    [InlineData(0, 2, 3, 2, 6, false)]
    [InlineData(3, 0, 3, 2, 6, false)]
    public void Snapshot_EnforcesExactAndOverOneKeyframePerFrameAndTotalBudgets(int count, int rectangles, int maximumFrames, int maximumPerFrame, int maximumTotal, bool succeeds)
    {
        var request = Request(count, rectangles);
        var limits = new VideoRepairLimits
        {
            MaximumMaskKeyframes = maximumFrames, MaximumRectanglesPerKeyframe = maximumPerFrame, MaximumMaskRectangles = maximumTotal
        };
        if (succeeds)
        {
            var snapshot = request.Snapshot(limits);
            Assert.Equal(count, snapshot.RectangleKeyframes!.Length);
            Assert.All(snapshot.RectangleKeyframes, frame => Assert.Equal(rectangles, frame.Rectangles.Length));
        }
        else Assert.Throws<ArgumentException>(() => request.Snapshot(limits));
    }

    [Fact]
    public void MaximumReportedCounts_AreRejectedBeforeIndexingEnumeratingOrAllocatingThem()
    {
        var request = new VideoWatermarkRemovalRequest { RectangleKeyframes = new CountOnlyList<VideoMaskKeyframe>(int.MaxValue) };
        Assert.Throws<ArgumentException>(() => request.Snapshot());
        request = new() { RectangleKeyframes = [new(TimeSpan.Zero, new CountOnlyList<VideoMaskRectangle>(int.MaxValue))] };
        Assert.Throws<ArgumentException>(() => request.Snapshot());
        Assert.Throws<ArgumentException>(() => request.Snapshot(new() { MaximumRectanglesPerKeyframe = int.MaxValue, MaximumMaskRectangles = 6 }));
    }

    [Fact]
    public void DefaultExactLimits_HaveDerivableSerializedSizeAndIntegerExtremesDoNotOverflow()
    {
        var limits = new VideoRepairLimits();
        var request = Request(limits.MaximumMaskKeyframes, limits.MaximumRectanglesPerKeyframe, extreme: true);
        var snapshot = request.Snapshot(limits);
        Assert.Equal(limits.MaximumMaskRectangles, snapshot.RectangleKeyframes!.Sum(frame => frame.Rectangles.Length));
        var json = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        Assert.True(json.Length <= 1024L + limits.MaximumMaskKeyframes * 128L + limits.MaximumMaskRectangles * 128L);
        var largeBudgets = limits with { MaximumMaskKeyframes = int.MaxValue, MaximumRectanglesPerKeyframe = int.MaxValue, MaximumMaskRectangles = int.MaxValue };
        Assert.Equal(2, Request(2, 2).Snapshot(largeBudgets).RectangleKeyframes!.Length);
    }

    [Fact]
    public void Snapshot_DetachesBothListLevelsBeforeTheRequestCanQueue()
    {
        var rectangles = new List<VideoMaskRectangle> { new(0, 0, 10, 10) };
        var keyframes = new List<VideoMaskKeyframe> { new(TimeSpan.Zero, rectangles), new(TimeSpan.FromTicks(1), rectangles) };
        var snapshot = new VideoWatermarkRemovalRequest { RectangleKeyframes = keyframes }.Snapshot();
        rectangles[0] = new(int.MaxValue, 0, int.MaxValue, 1);
        rectangles.Add(new(0, 0, 1, 1));
        keyframes.Clear();
        Assert.Equal(2, snapshot.RectangleKeyframes!.Length);
        Assert.All(snapshot.RectangleKeyframes, frame => Assert.Equal(new VideoMaskRectangle(0, 0, 10, 10), Assert.Single(frame.Rectangles)));
    }

    [Fact]
    public async Task ConfiguredRequestBudgets_AreAppliedBeforeReadingSourceOrCreatingWorkspace()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ms-video-request-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var model = Path.Combine(directory, "model");
            await File.WriteAllBytesAsync(model, [0]);
            var parent = Path.Combine(directory, "uncreated-parent");
            foreach (var limits in new[] { new VideoRepairLimits { MaximumMaskKeyframes = 2 }, new() { MaximumRectanglesPerKeyframe = 1 }, new() { MaximumMaskRectangles = 5 } })
            {
                await using var remover = new VideoWatermarkRemover(new() { PythonExecutablePath = Environment.ProcessPath!, MiganModelPath = model, Limits = limits });
                await Assert.ThrowsAsync<ArgumentException>(() => remover.RemoveAsync(Path.Combine(directory, "missing-source"), Path.Combine(parent, "result"), Request(3, 2)));
                Assert.False(Directory.Exists(parent));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static VideoWatermarkRemovalRequest Request(int count, int rectangles, bool extreme = false) => new()
    {
        RectangleKeyframes = Enumerable.Range(0, count).Select(index => new VideoMaskKeyframe(TimeSpan.FromTicks(index),
            Enumerable.Range(0, rectangles).Select(_ => extreme ? new VideoMaskRectangle(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue) : new(0, 0, 1, 1)).ToArray())).ToArray()
    };

    private sealed class CountOnlyList<T>(int count) : IReadOnlyList<T>
    {
        public int Count => count;
        public T this[int index] => throw new InvalidOperationException("An excessive count must be rejected before reading items.");
        public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException("An excessive count must be rejected before enumerating items.");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
