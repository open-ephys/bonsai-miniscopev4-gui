using Bonsai;
using OpenCV.Net;
using System;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Computes a running per-pixel maximum-intensity projection over the input image sequence,
/// emitting the accumulated projection for each input frame. Emit <see langword="true"/> on the
/// reset sequence to clear the accumulation (the next frame reseeds it).
/// </summary>
[Combinator]
[Description("Computes a running per-pixel maximum-intensity projection over the input image sequence.")]
public class CalculateMaxProjection
{
    /// <summary>
    /// Calculates the max intensity projection continuously until a <paramref name="reset"/> value is given.
    /// </summary>
    /// <param name="source">Incoming image source.</param>
    /// <param name="reset">Incoming <see cref="MaxProjectionSettings"/> value.</param>
    /// <returns></returns>
    public IObservable<IplImage> Process(IObservable<IplImage> source, IObservable<MaxProjectionSettings> reset)
    {
        return Observable.Create<IplImage>(observer =>
        {
            IplImage accumulator = null;
            int resetPending = 0;

            var resetSubscription = reset.Subscribe(
                value =>
                {
                    // NB: Toggle the resetPending flag to indicate that the next frame should reset the accumulator.
                    if (value.Reset) { Volatile.Write(ref resetPending, 1); }
                },
                observer.OnError,
                observer.OnCompleted);

            var sourceSubscription = source.Subscribe(
                input =>
                {
                    if (Interlocked.Exchange(ref resetPending, 0) == 1)
                    {
                        accumulator?.Dispose();
                        accumulator = null;
                    }

                    if (accumulator == null ||
                        accumulator.Width != input.Width ||
                        accumulator.Height != input.Height ||
                        accumulator.Depth != input.Depth ||
                        accumulator.Channels != input.Channels)
                    {
                        accumulator?.Dispose();
                        accumulator = input.Clone();
                    }
                    else
                    {
                        CV.Max(accumulator, input, accumulator);
                    }
                    observer.OnNext(accumulator);
                },
                observer.OnError,
                observer.OnCompleted);

            return new CompositeDisposable(
                resetSubscription,
                sourceSubscription,
                Disposable.Create(() => { accumulator?.Dispose(); accumulator = null; }));
        });
    }
}