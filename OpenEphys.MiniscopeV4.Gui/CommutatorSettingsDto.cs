using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Represents the commutator settings displayed and edited by the GUI.
/// </summary>
/// <param name="PortName">The name of the serial port the commutator is connected to.</param>
/// <param name="IsConnected">Whether the commutator serial port is currently open.</param>
/// <param name="Enable">Whether the commutator motor is enabled.</param>
/// <param name="EnableLed">Whether the commutator indication LED is enabled.</param>
public record CommutatorSettingsDto(string PortName, bool IsConnected, bool Enable, bool EnableLed);

/// <summary>
/// Combines individual commutator setting values into a single <see cref="CommutatorSettingsDto"/>.
/// </summary>
[Description("Combines individual commutator setting values into a single object.")]
[Combinator]
public class CreateCommutatorSettingsDto
{
    /// <summary>
    /// Creates a <see cref="CommutatorSettingsDto"/> by combining the latest values from each individual commutator setting sequence.
    /// </summary>
    /// <param name="portName">The name of the serial port the commutator is connected to.</param>
    /// <param name="isConnected">Whether the commutator serial port is currently open.</param>
    /// <param name="enable">Whether the commutator motor is enabled.</param>
    /// <param name="enableLed">Whether the commutator indication LED is enabled.</param>
    /// <returns>A sequence of <see cref="CommutatorSettingsDto"/> objects.</returns>
    public IObservable<CommutatorSettingsDto> Process(
        IObservable<string> portName,
        IObservable<bool> isConnected,
        IObservable<bool> enable,
        IObservable<bool> enableLed)
    {
        return Observable.CombineLatest(
            portName,
            isConnected,
            enable,
            enableLed,
            (portName, isConnected, enable, enableLed) => new CommutatorSettingsDto(portName, isConnected, enable, enableLed));
    }
}
