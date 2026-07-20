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
/// <param name="AutoConnect">Whether the commutator should automatically connect when acquisition starts, provided a valid COM port is selected.</param>
public record CommutatorSettings(string PortName, bool IsConnected, bool Enable, bool EnableLed, bool AutoConnect = true);

/// <summary>
/// Combines individual commutator setting values into a single <see cref="CommutatorSettings"/>.
/// </summary>
[Description("Combines individual commutator setting values into a single object.")]
[Combinator]
public class CreateCommutatorSettings
{
    /// <summary>
    /// Creates a <see cref="CommutatorSettings"/> by combining the latest values from each individual commutator setting sequence.
    /// </summary>
    /// <param name="portName">The name of the serial port the commutator is connected to.</param>
    /// <param name="isConnected">Whether the commutator serial port is currently open.</param>
    /// <param name="enable">Whether the commutator motor is enabled.</param>
    /// <param name="enableLed">Whether the commutator indication LED is enabled.</param>
    /// <param name="autoConnect">Whether the commutator should automatically connect when acquisition starts, provided a valid COM port is selected.</param>
    /// <returns>A sequence of <see cref="CommutatorSettings"/> objects.</returns>
    public IObservable<CommutatorSettings> Process(
        IObservable<string> portName,
        IObservable<bool> isConnected,
        IObservable<bool> enable,
        IObservable<bool> enableLed,
        IObservable<bool> autoConnect)
    {
        return Observable.CombineLatest(
            portName,
            isConnected,
            enable,
            enableLed,
            autoConnect,
            (portName, isConnected, enable, enableLed, autoConnect) => new CommutatorSettings(portName, isConnected, enable, enableLed, autoConnect));
    }
}
