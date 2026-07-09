using Bonsai;
using System;
using System.ComponentModel;
using System.Reactive.Linq;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Represents the max pixel-value projection settings edited in the Max Projection tab.
/// </summary>
/// <param name="Reset">
/// Raised for a single frame when the user clicks the Reset button, to reset the accumulation.
/// </param>
public record MaxProjectionSettings(bool Reset);


/// <summary>
/// Combines individual saturation setting values into a single <see cref="SaturationSettings"/>.
/// </summary>
[Description("Combines individual saturation setting values into a single object.")]
[Combinator]
public class CreateMaxProjectionSettings
{
    /// <summary>
    /// Creates a <see cref="MaxProjectionSettings"/> by combining the latest values from each individual saturation setting sequence.
    /// </summary>
    /// <param name="reset">The reset signal.</param>
    /// <returns>A sequence of <see cref="MaxProjectionSettings"/> objects.</returns>
    public IObservable<MaxProjectionSettings> Process(
        IObservable<bool> reset)
    {
        return reset.Select(reset => new MaxProjectionSettings(reset));
    }
}
