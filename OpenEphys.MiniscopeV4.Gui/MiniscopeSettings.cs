using System;

namespace OpenEphys.MiniscopeV4.Gui;

partial class MiniscopeSettings : IEquatable<MiniscopeSettings>
{
    /// <inheritdoc/>
    public bool Equals(MiniscopeSettings other) =>
        other is not null &&
        LedBrightness == other.LedBrightness &&
        Focus == other.Focus &&
        SensorGain == other.SensorGain &&
        FrameRate == other.FrameRate &&
        LedRespectsDigitalIn == other.LedRespectsDigitalIn;

    /// <inheritdoc/>
    public override bool Equals(object obj) => Equals(obj as MiniscopeSettings);

    /// <inheritdoc/>
    public override int GetHashCode() => (LedBrightness, Focus, SensorGain, FrameRate, LedRespectsDigitalIn).GetHashCode();
}
