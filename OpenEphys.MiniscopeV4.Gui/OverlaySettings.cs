using Bonsai;
using System;
using System.ComponentModel;
using System.Reactive.Linq;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Represents the settings edited in the Overlay tab: the screenshot request and destination folder,
/// and the reference-image overlay state.
/// </summary>
/// <param name="Capture">Raised for a single frame when the user clicks the Screenshot button.</param>
/// <param name="ApplyOverlay">Indicates whether the reference image should be applied as an overlay.</param>
/// <param name="ReferencePath">The path to the reference image file to overlay.</param>
public record OverlaySettings(bool Capture, bool ApplyOverlay, string ReferencePath);

/// <summary>
/// Combines individual overlay setting values into a single <see cref="OverlaySettings"/>.
/// </summary>
[Description("Combines individual overlay setting values into a single object.")]
[Combinator]
public class CreateOverlaySettings
{
    /// <summary>
    /// Creates an <see cref="OverlaySettings"/> by combining the latest values from each individual overlay setting sequence.
    /// </summary>
    /// <param name="capture">The screenshot capture signal.</param>
    /// <param name="applyOverlay">Indicates whether the reference image should be applied as an overlay.</param>
    /// <param name="referencePath">The path to the reference image file to overlay.</param>
    /// <returns>A sequence of <see cref="OverlaySettings"/> objects.</returns>
    public IObservable<OverlaySettings> Process(
        IObservable<bool> capture,
        IObservable<bool> applyOverlay,
        IObservable<string> referencePath)
    {
        return Observable.CombineLatest(
            capture,
            applyOverlay,
            referencePath,
            (capture, applyOverlay, referencePath) => new OverlaySettings(capture, applyOverlay, referencePath));
    }
}
