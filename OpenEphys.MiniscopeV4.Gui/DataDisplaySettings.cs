using System;

namespace OpenEphys.MiniscopeV4.Gui;

partial class DataDisplaySettings : IEquatable<DataDisplaySettings>
{
    /// <inheritdoc/>
    public bool Equals(DataDisplaySettings other) =>
        other is not null &&
        BufferSize == other.BufferSize &&
        Equals(Saturation, other.Saturation) &&
        Equals(Dff, other.Dff) &&
        Equals(MaxProjection, other.MaxProjection) &&
        Equals(Overlay, other.Overlay);

    /// <inheritdoc/>
    public override bool Equals(object obj) => Equals(obj as DataDisplaySettings);

    /// <inheritdoc/>
    public override int GetHashCode() => (BufferSize, Saturation, Dff, MaxProjection, Overlay).GetHashCode();
}
