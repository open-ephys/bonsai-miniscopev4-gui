using System;
using YamlDotNet.Serialization;

namespace OpenEphys.MiniscopeV4.Gui;

partial class CameraStatus : IEquatable<CameraStatus>
{
    /// <summary>Whether the miniscope is currently connected.</summary>
    [YamlIgnore]
    public bool IsConnected { get; set; }

    /// <summary>
    /// Whether the data display is frozen. When <see langword="true"/>, the workflow stops sampling new
    /// frames into the image and signal panels while data acquisition continues unaffected.
    /// </summary>
    [YamlIgnore]
    public bool Paused { get; set; }

    /// <inheritdoc/>
    public bool Equals(CameraStatus other) =>
        other is not null &&
        CameraIndex == other.CameraIndex &&
        IsConnected == other.IsConnected &&
        Paused == other.Paused;

    /// <inheritdoc/>
    public override bool Equals(object obj) => Equals(obj as CameraStatus);

    /// <inheritdoc/>
    public override int GetHashCode() => (CameraIndex, IsConnected, Paused).GetHashCode();
}
