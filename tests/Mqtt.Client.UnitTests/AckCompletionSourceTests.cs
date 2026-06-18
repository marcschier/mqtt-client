// Copyright (c) 2026 marcschier. Licensed under the MIT License.

namespace Mqtt.Client.UnitTests;

public class AckCompletionSourceTests
{
    [Test]
    public async Task Resolves_with_result()
    {
        var w = AckCompletionSource.Rent(CancellationToken.None);
        var vt = w.ValueTask;
        await Assert.That(w.TrySetResult("hello")).IsTrue();
        var result = await vt;
        await Assert.That(result).IsEqualTo((object?)"hello");
    }

    [Test]
    public async Task Second_completion_is_ignored()
    {
        var w = AckCompletionSource.Rent(CancellationToken.None);
        var vt = w.ValueTask;
        await Assert.That(w.TrySetResult(1)).IsTrue();
        await Assert.That(w.TrySetResult(2)).IsFalse();
        await Assert.That(w.TrySetException(new InvalidOperationException())).IsFalse();
        await Assert.That(await vt).IsEqualTo((object?)1);
    }

    [Test]
    public async Task Exception_propagates()
    {
        var w = AckCompletionSource.Rent(CancellationToken.None);
        var vt = w.ValueTask;
        w.TrySetException(new InvalidOperationException("boom"));
        await Assert.That(async () => await vt).ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task Cancellation_completes_with_operation_canceled()
    {
        using var cts = new CancellationTokenSource();
        var w = AckCompletionSource.Rent(cts.Token);
        var vt = w.ValueTask;
        cts.Cancel();
        await Assert.That(async () => await vt).ThrowsExactly<OperationCanceledException>();
    }

    [Test]
    public async Task Instance_is_reusable_after_completion()
    {
        // Complete and observe one cycle, then rent again and confirm it works (pool reuse path).
        var first = AckCompletionSource.Rent(CancellationToken.None);
        var firstVt = first.ValueTask;
        first.TrySetResult("a");
        await firstVt;

        var second = AckCompletionSource.Rent(CancellationToken.None);
        var secondVt = second.ValueTask;
        second.TrySetResult("b");
        await Assert.That(await secondVt).IsEqualTo((object?)"b");
    }
}
