using MS.Microservice.Threading;
using MS.Microservice.Infrastructure.Common.NAudio;
using NSubstitute;
using Xunit;
namespace MS.Microservice.Threading.Tests;
public class BatchTests
{
    [Fact] public async Task AudioFaultTakesPrecedenceOverItemCancellation()
    {
        var directory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
        var processor=Substitute.For<IAudioProcessor>();
        processor.ConvertAudioFormatAsync(Arg.Any<string>(),Arg.Any<string>(),Arg.Any<AudioConvertOptions>())
            .Returns(call=>ValueTask.FromException((string)call[0]=="cancel.wav" ? new OperationCanceledException() : new IOException("failure")));
        try { await Assert.ThrowsAsync<IOException>(()=>processor.BatchConvertAsync(["cancel.wav","fault.wav"],directory,AudioFormat.Wav)); }
        finally { Directory.Delete(directory,true); }
    }
    [Fact] public async Task BoundsConcurrencyAndRetainsOrderAndFailures()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int running = 0, maximum = 0, started = 0;
        var pending = BatchProcessing.CollectAsync<int,int>([0,1,2,3], async (item, token) =>
        {
            var count = Interlocked.Increment(ref running);
            lock (release) maximum = Math.Max(maximum,count);
            if (Interlocked.Increment(ref started)==2) entered.SetResult();
            try { await release.Task.WaitAsync(token); if(item==1) throw new IOException("item"); return item*2; }
            finally { Interlocked.Decrement(ref running); }
        },2);
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(2,started); }
        finally { release.TrySetResult(); }
        var results=await pending;
        Assert.Equal(2,maximum); Assert.Equal(new[]{0,1,2,3},results.Select(x=>x.Index));
        Assert.IsType<IOException>(results[1].Error); Assert.Equal(6,results[3].Value);
    }
    [Fact] public async Task CancellationWaitsForInFlightAndStopsDispatch()
    {
        using var stop=new CancellationTokenSource();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int started=0;
        var pending=BatchProcessing.CollectAsync<int,int>([1,2],async (item,token)=>
        { Interlocked.Increment(ref started); entered.SetResult(); await release.Task; return item; },1,stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel();
        Assert.False(pending.IsCompleted); release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>pending); Assert.Equal(1,started);
    }
    [Fact] public async Task EmptyAndInvalidConcurrency()
    {
        Assert.Empty(await BatchProcessing.CollectAsync<int,int>([],(x,t)=>ValueTask.FromResult(x),1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>BatchProcessing.CollectAsync<int,int>([],(x,t)=>ValueTask.FromResult(x),0));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task AudioLegacyEntryPointsStillCompleteOtherItemsAndPropagateFailure(bool alias)
    {
        var directory=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());
        var processor=Substitute.For<IAudioProcessor>();
        int called=0;
        processor.ConvertAudioFormatAsync(Arg.Any<string>(),Arg.Any<string>(),Arg.Any<AudioConvertOptions>())
            .Returns(call=> { Interlocked.Increment(ref called); return (string)call[0]=="bad.wav" ? ValueTask.FromException(new IOException("bad")) : ValueTask.CompletedTask; });
        try
        {
            await Assert.ThrowsAsync<IOException>(()=>alias
                ? processor.BatchConvertAudioFormatAsync(["bad.wav","good.wav"],directory,AudioFormat.Wav)
                : processor.BatchConvertAsync(["bad.wav","good.wav"],directory,AudioFormat.Wav));
            Assert.Equal(2,called);
        }
        finally { Directory.Delete(directory,true); }
    }
}
