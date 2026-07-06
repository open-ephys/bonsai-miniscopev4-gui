using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;
using OpenEphys.Miniscope;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Represents the Miniscope acquisition settings displayed and edited by the GUI.
/// </summary>
/// <param name="LedBrightness">The brightness of the excitation LED, as a percentage.</param>
/// <param name="Focus">The electrowetting lens focus value.</param>
/// <param name="SensorGain">The image sensor gain.</param>
/// <param name="FrameRate">The image sensor frame rate.</param>
/// <param name="LedRespectsDigitalIn">The digital input line that gates the excitation LED, if any.</param>
public record MiniscopeSettings(double LedBrightness, double Focus, GainV4 SensorGain, FrameRateV4 FrameRate, MiniscopeDaqDigitalIn LedRespectsDigitalIn);

/// <summary>
/// Combines individual Miniscope setting values into a single <see cref="MiniscopeSettings"/>.
/// </summary>
[Description("Combines individual Miniscope setting values into a single object.")]
[Combinator]
public class CreateMiniscopeSettings
{
    /// <summary>
    /// Creates a <see cref="MiniscopeSettings"/> by combining the latest values from each individual Miniscope setting sequence.
    /// </summary>
    /// <param name="ledBrightness">The brightness of the excitation LED, as a percentage.</param>
    /// <param name="focus">The electrowetting lens focus value.</param>
    /// <param name="sensorGain">The image sensor gain.</param>
    /// <param name="frameRate">The image sensor frame rate.</param>
    /// <param name="ledRespectsDigitalIn">The digital input line that gates the excitation LED, if any.</param>
    /// <returns>A sequence of <see cref="MiniscopeSettings"/> objects.</returns>
    public IObservable<MiniscopeSettings> Process(
        IObservable<double> ledBrightness,
        IObservable<double> focus,
        IObservable<GainV4> sensorGain,
        IObservable<FrameRateV4> frameRate,
        IObservable<MiniscopeDaqDigitalIn> ledRespectsDigitalIn)
    {
        return Observable.CombineLatest(
            ledBrightness,
            focus,
            sensorGain,
            frameRate,
            ledRespectsDigitalIn,
            (ledBrightness, focus, sensorGain, frameRate, ledRespectsDigitalIn) => new MiniscopeSettings(ledBrightness, focus, sensorGain, frameRate, ledRespectsDigitalIn));
    }
}
