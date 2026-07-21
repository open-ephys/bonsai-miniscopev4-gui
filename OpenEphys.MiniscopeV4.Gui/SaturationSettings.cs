using System;

namespace OpenEphys.MiniscopeV4.Gui;

partial class SaturationSettings : IEquatable<SaturationSettings>
{
    /// <inheritdoc/>
    public bool Equals(SaturationSettings other) =>
        other is not null &&
        Threshold == other.Threshold &&
        Color.Equals(other.Color);

    /// <inheritdoc/>
    public override bool Equals(object obj) => Equals(obj as SaturationSettings);

    /// <inheritdoc/>
    public override int GetHashCode() => (Threshold, Color).GetHashCode();
}
