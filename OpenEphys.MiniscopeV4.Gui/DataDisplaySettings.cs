using System;
using YamlDotNet.Serialization;

namespace OpenEphys.MiniscopeV4.Gui;

partial class DataDisplaySettings : IEquatable<DataDisplaySettings>
{
    const int DefaultBufferSize = 1200;

    /// <summary>
    /// The buffer size, in samples, used by the time series plots.
    /// </summary>
    [YamlIgnore]
    public int BufferSize { get; set; } = DefaultBufferSize;

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
