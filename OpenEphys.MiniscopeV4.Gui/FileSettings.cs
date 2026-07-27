using System;
using YamlDotNet.Serialization;

namespace OpenEphys.MiniscopeV4.Gui;

partial class FileSettings : IEquatable<FileSettings>
{
    /// <summary>
    /// Indicates whether the record button is engaged. When RecordingMode is Trigger this arms
    /// recording; otherwise it starts recording directly.
    /// </summary>
    [YamlIgnore]
    public bool RecordButton { get; set; }

    /// <inheritdoc/>
    public bool Equals(FileSettings other) =>
        other is not null &&
        RecordButton == other.RecordButton &&
        RecordingMode == other.RecordingMode &&
        CompressVideo == other.CompressVideo &&
        FileName == other.FileName &&
        Suffix == other.Suffix &&
        RecordingDuration == other.RecordingDuration &&
        TotalDuration == other.TotalDuration &&
        SegmentMode == other.SegmentMode &&
        TriggerInput == other.TriggerInput;

    /// <inheritdoc/>
    public override bool Equals(object obj) => Equals(obj as FileSettings);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        (RecordButton, RecordingMode, CompressVideo, FileName, Suffix, RecordingDuration, TotalDuration, SegmentMode, TriggerInput).GetHashCode();
}
