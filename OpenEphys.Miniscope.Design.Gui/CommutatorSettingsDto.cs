using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

namespace OpenEphys.Miniscope.Design.Gui;

/// <summary>
/// Represents the commutator settings displayed and edited by the GUI.
/// </summary>
/// <param name="PortName">The name of the serial port the commutator is connected to.</param>
/// <param name="IsConnected">Whether the commutator serial port is currently open.</param>
/// <param name="Enable">Whether the commutator motor is enabled.</param>
/// <param name="EnableLed">Whether the commutator indication LED is enabled.</param>
/// <param name="StatusMessage">A message describing the current commutator status, shown in the status bar.</param>
public record CommutatorSettingsDto(string PortName, bool IsConnected, bool Enable, bool EnableLed, string StatusMessage);

/// <summary>
/// Combines individual commutator setting values into a single <see cref="CommutatorSettingsDto"/>.
/// </summary>
[Description("Combines individual commutator setting values into a single object.")]
public class CreateCommutatorSettingsDto : Transform<Tuple<string, bool, bool, bool, string>, CommutatorSettingsDto>
{
    /// <summary>
    /// Creates a <see cref="CommutatorSettingsDto"/> from a sequence of tuples containing the commutator settings.
    /// </summary>
    /// <param name="source">A sequence of tuples containing the port name, connection state, enable, LED enable, and status message values.</param>
    /// <returns>A sequence of <see cref="CommutatorSettingsDto"/> objects.</returns>
    public override IObservable<CommutatorSettingsDto> Process(IObservable<Tuple<string, bool, bool, bool, string>> source)
    {
        return source.Select(value => new CommutatorSettingsDto(value.Item1, value.Item2, value.Item3, value.Item4, value.Item5));
    }
}
