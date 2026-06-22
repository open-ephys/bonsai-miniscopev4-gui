using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;
using OpenCV.Net;

namespace OpenEphys.Miniscope.Design.Gui;

/// <summary>
/// Represents the saturation overlay settings displayed and edited by the GUI.
/// </summary>
/// <param name="Threshold">The minimum pixel intensity considered saturated.</param>
/// <param name="Color">The color used to highlight saturated pixels.</param>
public record SaturationSettingsDto(double Threshold, Scalar Color);

/// <summary>
/// Combines individual saturation setting values into a single <see cref="SaturationSettingsDto"/>.
/// </summary>
[Description("Combines individual saturation setting values into a single object.")]
public class CreateSaturationSettingsDto : Transform<Tuple<double, Scalar>, SaturationSettingsDto>
{
    /// <summary>
    /// Creates a <see cref="SaturationSettingsDto"/> from a sequence of tuples containing the saturation settings.
    /// </summary>
    /// <param name="source">A sequence of tuples containing the threshold and color values.</param>
    /// <returns>A sequence of <see cref="SaturationSettingsDto"/> objects.</returns>
    public override IObservable<SaturationSettingsDto> Process(IObservable<Tuple<double, Scalar>> source)
    {
        return source.Select(value => new SaturationSettingsDto(value.Item1, value.Item2));
    }
}
