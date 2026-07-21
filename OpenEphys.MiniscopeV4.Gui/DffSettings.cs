using System;

namespace OpenEphys.MiniscopeV4.Gui;

partial class DffSettings : IEquatable<DffSettings>
{
    /// <inheritdoc/>
    public bool Equals(DffSettings other) =>
        other is not null &&
        BackgroundFrames == other.BackgroundFrames &&
        BackgroundThreshold == other.BackgroundThreshold &&
        Sigma == other.Sigma;

    /// <inheritdoc/>
    public override bool Equals(object obj) => Equals(obj as DffSettings);

    /// <inheritdoc/>
    public override int GetHashCode() => (BackgroundFrames, BackgroundThreshold, Sigma).GetHashCode();
}
