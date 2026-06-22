using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

namespace OpenEphys.Miniscope.Design.Gui;

/// <summary>
/// Represents the Miniscope acquisition settings displayed and edited by the GUI.
/// </summary>
/// <param name="LedBrightness">The brightness of the excitation LED, as a percentage.</param>
/// <param name="Focus">The electrowetting lens focus value.</param>
/// <param name="SensorGain">The image sensor gain.</param>
/// <param name="FrameRate">The image sensor frame rate.</param>
/// <param name="LedRespectsDigitalIn">The digital input line that gates the excitation LED, if any.</param>
public record MiniscopeSettingsDto(double LedBrightness, double Focus, GainV4 SensorGain, FrameRateV4 FrameRate, MiniscopeDaqDigitalIn LedRespectsDigitalIn);

/// <summary>
/// Combines individual Miniscope setting values into a single <see cref="MiniscopeSettingsDto"/>.
/// </summary>
[Description("Combines individual Miniscope setting values into a single object.")]
public class CreateMiniscopeSettingsDto : Transform<Tuple<double, double, GainV4, FrameRateV4, MiniscopeDaqDigitalIn>, MiniscopeSettingsDto>
{
    /// <summary>
    /// Creates a <see cref="MiniscopeSettingsDto"/> from a sequence of tuples containing the Miniscope settings.
    /// </summary>
    /// <param name="source">A sequence of tuples containing the Miniscope settings.</param>
    /// <returns>A sequence of <see cref="MiniscopeSettingsDto"/> objects.</returns>
    public override IObservable<MiniscopeSettingsDto> Process(IObservable<Tuple<double, double, GainV4, FrameRateV4, MiniscopeDaqDigitalIn>> source)
    {
        return source.Select(value => new MiniscopeSettingsDto(value.Item1, value.Item2, value.Item3, value.Item4, value.Item5));
    }
}
