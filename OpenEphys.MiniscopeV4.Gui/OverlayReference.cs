using Bonsai;
using OpenCV.Net;
using System;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Superimposes a saved reference image over the live image using a multi-color overlay: each grayscale
/// source is tinted by its own user-selected color (<see cref="OverlaySettings.ReferenceColor"/> and
/// <see cref="OverlaySettings.LiveColor"/>) and the tinted results are added together, so overlapping
/// bright regions blend towards the combination of both colors.
/// </summary>
[Combinator]
[Description("Superimposes a reference image over the live image, tinting each with its own configurable color.")]
public class OverlayReference
{
    /// <summary>
    /// Composites the reference image over each input frame while the overlay is enabled.
    /// </summary>
    /// <param name="source">Incoming <see cref="IplImage"/>, and <see cref="OverlaySettings"/> source.</param>
    /// <param name="logSource">The shared <see cref="MiniscopeLog"/> instance (typically a <c>BehaviorSubject</c>), captured once and used to log messages.</param>
    /// <returns>The composited image while enabled, otherwise the input image unchanged.</returns>
    public IObservable<IplImage> Process(IObservable<Tuple<IplImage, OverlaySettings>> source, IObservable<MiniscopeLog> logSource)
    {
        return Observable.Create<IplImage>(observer =>
        {
            IplImage referenceImage = null;
            IplImage currentImage = null;
            IplImage compositeImage = null;
            IplImage scratchReference = null;
            IplImage scratchLive = null;
            IplImage[] channelBuffers = null;
            string cachedPath = null;

            // NB: Expect this to be a BehaviorSubject, so we can take the first value immediately.
            MiniscopeLog log = null;
            var logSubscription = logSource.Take(1).Subscribe(value => log = value);

            if (log == null)
            {
                throw new InvalidOperationException("No MiniscopeLog instance was provided.");
            }

            var subscription = source.Subscribe(
                input =>
                {
                    var image = input.Item1;
                    var overlay = input.Item2;

                    if (overlay.ReferencePath != cachedPath)
                    {
                        cachedPath = overlay.ReferencePath;
                        referenceImage?.Dispose();
                        referenceImage = LoadReference(cachedPath, image, log);
                    }

                    if (referenceImage == null)
                    {
                        return;
                    }

                    if (!overlay.ApplyOverlay)
                    {
                        observer.OnNext(referenceImage);
                        return;
                    }

                    if (currentImage == null || currentImage.Width != image.Width || currentImage.Height != image.Height)
                    {
                        currentImage?.Dispose();
                        currentImage = new IplImage(image.Size, IplDepth.U8, 1);
                        scratchReference?.Dispose();
                        scratchReference = new IplImage(image.Size, IplDepth.U8, 1);
                        scratchLive?.Dispose();
                        scratchLive = new IplImage(image.Size, IplDepth.U8, 1);

                        if (channelBuffers != null)
                        {
                            foreach (var buffer in channelBuffers)
                                buffer.Dispose();
                        }

                        channelBuffers = new[]
                        {
                            new IplImage(image.Size, IplDepth.U8, 1),
                            new IplImage(image.Size, IplDepth.U8, 1),
                            new IplImage(image.Size, IplDepth.U8, 1),
                        };
                    }

                    if (compositeImage == null || compositeImage.Width != image.Width || compositeImage.Height != image.Height)
                    {
                        compositeImage?.Dispose();
                        compositeImage = new IplImage(image.Size, IplDepth.U8, 3);
                        compositeImage.SetZero();
                    }

                    CV.CvtColor(image, currentImage, ColorConversion.Bgr2Gray);

                    // NB: BGR channel order. Each grayscale source is scaled by its color's weight for that
                    // channel, then the two scaled images are added together (saturating), so overlapping
                    // bright regions blend towards the additive combination of both colors.
                    Span<double> referenceWeights = stackalloc double[]
                    {
                        overlay.ReferenceColor.Val0 / 255.0,
                        overlay.ReferenceColor.Val1 / 255.0,
                        overlay.ReferenceColor.Val2 / 255.0,
                    };
                    Span<double> liveWeights = stackalloc double[]
                    {
                        overlay.LiveColor.Val0 / 255.0,
                        overlay.LiveColor.Val1 / 255.0,
                        overlay.LiveColor.Val2 / 255.0,
                    };

                    for (int channel = 0; channel < 3; channel++)
                    {
                        CV.ConvertScale(referenceImage, scratchReference, referenceWeights[channel], 0);
                        CV.ConvertScale(currentImage, scratchLive, liveWeights[channel], 0);
                        CV.Add(scratchReference, scratchLive, channelBuffers[channel], null);
                    }

                    CV.Merge(channelBuffers[0], channelBuffers[1], channelBuffers[2], null, compositeImage);
                    observer.OnNext(compositeImage);
                },
                observer.OnError,
                observer.OnCompleted);

            return new CompositeDisposable(
                subscription,
                Disposable.Create(() =>
                {
                    referenceImage?.Dispose(); referenceImage = null;
                    currentImage?.Dispose(); currentImage = null;
                    compositeImage?.Dispose(); compositeImage = null;
                    scratchReference?.Dispose(); scratchReference = null;
                    scratchLive?.Dispose(); scratchLive = null;
                    if (channelBuffers != null)
                    {
                        foreach (var buffer in channelBuffers)
                            buffer.Dispose();
                        channelBuffers = null;
                    }
                }));
        });
    }

    static IplImage LoadReference(string path, IplImage frame, MiniscopeLog log)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        IplImage loaded;
        try
        {
            loaded = CV.LoadImage(path, LoadImageFlags.Grayscale);
        }
        catch (Exception ex)
        {
            log.Warning($"Could not load reference image '{path}': {ex.Message}");
            return null;
        }

        if (loaded == null)
        {
            log.Warning($"Could not load reference image '{path}'.");
            return null;
        }

        if (loaded.Width != frame.Width || loaded.Height != frame.Height)
        {
            log.Warning($"Reference image '{path}' is the wrong size. Expected [{frame.Width}x{frame.Height}], but got {loaded.Width}x{loaded.Height}.");
            loaded.Dispose();
            return null;
        }

        return loaded;
    }
}
