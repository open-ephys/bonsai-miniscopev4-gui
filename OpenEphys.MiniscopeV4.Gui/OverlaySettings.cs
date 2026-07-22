using System;
using YamlDotNet.Serialization;

namespace OpenEphys.MiniscopeV4.Gui;

partial class OverlaySettings : IEquatable<OverlaySettings>
{
    /// <summary>
    /// Raised when the user clicks the Screenshot button.
    /// </summary>
    [YamlIgnore]
    public bool Capture { get; set; }

    /// <inheritdoc/>
    public bool Equals(OverlaySettings other) =>
        other is not null &&
        Capture == other.Capture &&
        ApplyOverlay == other.ApplyOverlay &&
        ReferencePath == other.ReferencePath &&
        ReferenceColor == other.ReferenceColor &&
        LiveColor == other.LiveColor;

    /// <inheritdoc/>
    public override bool Equals(object obj) => Equals(obj as OverlaySettings);

    /// <inheritdoc/>
    public override int GetHashCode() => (Capture, ApplyOverlay, ReferencePath, ReferenceColor, LiveColor).GetHashCode();
}
