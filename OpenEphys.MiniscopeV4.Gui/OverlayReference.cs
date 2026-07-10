using Bonsai;
using OpenCV.Net;
using System;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Superimposes a saved reference image over the live image using a two-color overlay: the live frame
/// is rendered green and the reference is rendered magenta, so overlapping regions appear white. The
/// reference must match the live frame's dimensions; a mismatched or unreadable image is rejected with
/// a warning and the live frame is passed through unchanged.
/// </summary>
[Combinator]
[Description("Superimposes a reference image over the live image using a green/magenta two-color overlay.")]
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
                    }

                    if (compositeImage == null || compositeImage.Width != image.Width || compositeImage.Height != image.Height)
                    {
                        compositeImage?.Dispose();
                        compositeImage = new IplImage(image.Size, IplDepth.U8, 3);
                        compositeImage.SetZero();
                    }

                    CV.CvtColor(image, currentImage, ColorConversion.Bgr2Gray);

                    // NB: BGR channel order B = reference, G = live. Overlapping regions appear yellow
                    CV.Merge(referenceImage, currentImage, null, null, compositeImage);
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
