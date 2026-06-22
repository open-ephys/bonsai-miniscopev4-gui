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
public class CreateDffSettingsDto : Transform<Tuple<int, double, int>, DffSettingsDto>
{
    /// <summary>
    /// Creates a <see cref="DffSettingsDto"/> from a sequence of tuples containing the dF/F settings.
    /// </summary>
    /// <param name="source">A sequence of tuples containing the background frames, background threshold, and sigma values.</param>
    /// <returns>A sequence of <see cref="DffSettingsDto"/> objects.</returns>
    public override IObservable<DffSettingsDto> Process(IObservable<Tuple<int, double, int>> source)
    {
        return source.Select(value => new DffSettingsDto(value.Item1, value.Item2, value.Item3));
    }
}
