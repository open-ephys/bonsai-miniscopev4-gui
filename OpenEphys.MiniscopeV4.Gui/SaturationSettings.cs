using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;
using OpenCV.Net;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Represents the saturation overlay settings displayed and edited by the GUI.
/// </summary>
/// <param name="Threshold">The minimum pixel intensity considered saturated.</param>
/// <param name="Color">The color used to highlight saturated pixels.</param>
public record SaturationSettings(int Threshold, Scalar Color);

/// <summary>
/// Combines individual saturation setting values into a single <see cref="SaturationSettings"/>.
/// </summary>
[Description("Combines individual saturation setting values into a single object.")]
[Combinator]
public class CreateSaturationSettings
{
    /// <summary>
    /// Creates a <see cref="SaturationSettings"/> by combining the latest values from each individual saturation setting sequence.
    /// </summary>
    /// <param name="threshold">The minimum pixel intensity considered saturated.</param>
    /// <param name="color">The color used to highlight saturated pixels.</param>
    /// <returns>A sequence of <see cref="SaturationSettings"/> objects.</returns>
    public IObservable<SaturationSettings> Process(
        IObservable<int> threshold,
        IObservable<Scalar> color)
    {
        return Observable.CombineLatest(
            threshold,
            color,
            (threshold, color) => new SaturationSettings(threshold, color));
    }
}
