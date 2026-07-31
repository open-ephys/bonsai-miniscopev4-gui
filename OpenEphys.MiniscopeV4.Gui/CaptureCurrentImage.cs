using Bonsai;
using OpenCV.Net;
using OpenEphys.Miniscope;
using System;
using System.ComponentModel;
using System.IO;
using System.Reactive.Linq;
using System.Xml.Serialization;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Captures the current image and saves to an image file when requested, forwarding the frame
/// unchanged. When an explicit folder is set the frame is saved there with a timestamped name;
/// otherwise a clone of the frame is captured and a save dialog prompts for a destination.
/// </summary>
[Description("Captures the current image and saves to an image file when requested.")]
[Combinator]
[WorkflowElementCategory(ElementCategory.Sink)]
public class CaptureCurrentImage
{
    /// <summary>
    /// Gets or sets the data path where all data is saved.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public string DataPath { get; set; }

    /// <summary>
    /// Forwards each input <see cref="UclaMiniscopeV4Frame"/> and saves the image to disk.
    /// </summary>
    /// <param name="source">A sequence of <see cref="UclaMiniscopeV4Frame">frames</see> that are saved to disk and passed through unchanged.</param>
    /// <param name="logSource">The shared <see cref="MiniscopeLog"/> instance (typically a <c>BehaviorSubject</c>), captured once and used to log messages.</param>
    /// <returns>The input image sequence, unchanged.</returns>
    public IObservable<UclaMiniscopeV4Frame> Process(IObservable<UclaMiniscopeV4Frame> source, IObservable<MiniscopeLog> logSource)
    {
        return Observable.Create<UclaMiniscopeV4Frame>(observer =>
        {
            // NB: Expect this to be a BehaviorSubject, so we can take the first value immediately.
            MiniscopeLog log = null;
            var logSubscription = logSource.Take(1).Subscribe(value => log = value);

            if (log == null)
            {
                throw new InvalidOperationException("No MiniscopeLog instance was provided.");
            }

            return source.Subscribe(
                frame =>
                {
                    CaptureImage(frame.Image, frame.FrameNumber, DataPath, log);

                    observer.OnNext(frame);
                },
                observer.OnError,
                observer.OnCompleted);
        });
    }

    static void CaptureImage(IplImage image, int frameNumber, string folder, MiniscopeLog log)
    {
        try
        {
            var directory = FileDialogHelpers.GetDirectory(folder);
            Directory.CreateDirectory(directory);
            var path = folder + TimestampedName($"frame_{frameNumber}");
            CV.SaveImage(path, image);
            log.Info($"Saved image to {path}");
        }
        catch (Exception ex)
        {
            log.Error($"Could not save image: {ex.Message}");
        }
    }

    static string TimestampedName(string fileName) => $"{fileName}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
}
