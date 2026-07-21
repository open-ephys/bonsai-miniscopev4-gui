using System;

namespace OpenEphys.MiniscopeV4.Gui;

partial class MiniscopeConfiguration : IEquatable<MiniscopeConfiguration>
{
    /// <inheritdoc/>
    public bool Equals(MiniscopeConfiguration other) =>
        other is not null &&
        Equals(Camera, other.Camera) &&
        Equals(Hardware, other.Hardware) &&
        Equals(Display, other.Display) &&
        Equals(File, other.File);

    /// <inheritdoc/>
    public override bool Equals(object obj) => Equals(obj as MiniscopeConfiguration);

    /// <inheritdoc/>
    public override int GetHashCode() => (Camera, Hardware, Display, File).GetHashCode();
}
