using System;
using YamlDotNet.Serialization;

namespace OpenEphys.MiniscopeV4.Gui;

partial class MaxProjectionSettings : IEquatable<MaxProjectionSettings>
{
    /// <summary>
    /// Raised when the user clicks the Reset button, to reset the accumulation.
    /// </summary>
    [YamlIgnore]
    public bool Reset { get; set; }

    /// <inheritdoc/>
    public bool Equals(MaxProjectionSettings other) => other is not null && Reset == other.Reset;

    /// <inheritdoc/>
    public override bool Equals(object obj) => Equals(obj as MaxProjectionSettings);

    /// <inheritdoc/>
    public override int GetHashCode() => Reset.GetHashCode();
}
