using System;

namespace OpenEphys.MiniscopeV4.Gui;

partial class HardwareSettings : IEquatable<HardwareSettings>
{
    /// <inheritdoc/>
    public bool Equals(HardwareSettings other) =>
        other is not null &&
        Equals(Miniscope, other.Miniscope) &&
        Equals(Commutator, other.Commutator);

    /// <inheritdoc/>
    public override bool Equals(object obj) => Equals(obj as HardwareSettings);

    /// <inheritdoc/>
    public override int GetHashCode() => (Miniscope, Commutator).GetHashCode();
}
