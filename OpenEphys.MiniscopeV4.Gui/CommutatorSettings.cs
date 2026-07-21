using System;
using YamlDotNet.Serialization;

namespace OpenEphys.MiniscopeV4.Gui;

partial class CommutatorSettings : IEquatable<CommutatorSettings>
{
    /// <summary>
    /// Whether the commutator serial port is currently open.
    /// </summary>
    [YamlIgnore]
    public bool IsConnected { get; set; }

    /// <inheritdoc/>
    public bool Equals(CommutatorSettings other) =>
        other is not null &&
        PortName == other.PortName &&
        IsConnected == other.IsConnected &&
        Enable == other.Enable &&
        EnableLed == other.EnableLed &&
        AutoConnect == other.AutoConnect;

    /// <inheritdoc/>
    public override bool Equals(object obj) => Equals(obj as CommutatorSettings);

    /// <inheritdoc/>
    public override int GetHashCode() => (PortName, IsConnected, Enable, EnableLed, AutoConnect).GetHashCode();
}
