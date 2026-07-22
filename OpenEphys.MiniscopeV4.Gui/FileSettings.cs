using System;
using YamlDotNet.Serialization;

namespace OpenEphys.MiniscopeV4.Gui;

partial class FileSettings : IEquatable<FileSettings>
{
    /// <summary>
    /// Indicates whether the record button is engaged. When TriggerMode is false this starts recording
    /// directly; when true it arms recording.
    /// </summary>
    [YamlIgnore]
    public bool RecordButton { get; set; }

    /// <inheritdoc/>
    public bool Equals(FileSettings other) =>
        other is not null &&
        RecordButton == other.RecordButton &&
        TriggerMode == other.TriggerMode &&
        CompressVideo == other.CompressVideo &&
        FileName == other.FileName &&
        Suffix == other.Suffix &&
        RecordingDuration == other.RecordingDuration &&
        UseRecordDuration == other.UseRecordDuration &&
        TriggerInput == other.TriggerInput &&
        AutomaticRestart == other.AutomaticRestart;

    /// <inheritdoc/>
    public override bool Equals(object obj) => Equals(obj as FileSettings);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        (RecordButton, TriggerMode, CompressVideo, FileName, Suffix, RecordingDuration, UseRecordDuration, TriggerInput, AutomaticRestart).GetHashCode();
}
