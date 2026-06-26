using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

namespace OpenEphys.Miniscope.Design.Gui;

/// <summary>
/// Represents the dF/F (delta-F over F) calculation settings displayed and edited by the GUI.
/// </summary>
/// <param name="BackgroundFrames">The number of previous frames to average to determine the background fluorescence.</param>
/// <param name="BackgroundThreshold">The minimum background intensity level required to calculate delta-F/F.</param>
/// <param name="Sigma">The standard deviation, in pixels, of the Gaussian smoothing function. If set to 0, no smoothing is performed.</param>
public record DffSettingsDto(int BackgroundFrames, double BackgroundThreshold, int Sigma);

/// <summary>
/// Combines individual dF/F setting values into a single <see cref="DffSettingsDto"/>.
/// </summary>
[Description("Combines individual dF/F setting values into a single object.")]
[Combinator]
public class CreateDffSettingsDto
{
    /// <summary>
    /// Creates a <see cref="DffSettingsDto"/> by combining the latest values from each individual dF/F setting sequence.
    /// </summary>
    /// <param name="backgroundFrames">The number of previous frames to average to determine the background fluorescence.</param>
    /// <param name="backgroundThreshold">The minimum background intensity level required to calculate delta-F/F.</param>
    /// <param name="sigma">The standard deviation, in pixels, of the Gaussian smoothing function. If set to 0, no smoothing is performed.</param>
    /// <returns>A sequence of <see cref="DffSettingsDto"/> objects.</returns>
    public IObservable<DffSettingsDto> Process(
        IObservable<int> backgroundFrames,
        IObservable<double> backgroundThreshold,
        IObservable<int> sigma)
    {
        return Observable.CombineLatest(
            backgroundFrames,
            backgroundThreshold,
            sigma,
            (backgroundFrames, backgroundThreshold, sigma) => new DffSettingsDto(backgroundFrames, backgroundThreshold, sigma));
    }
}
