using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;
using Bonsai.IO;
using OpenEphys.Miniscope;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Represents the file saving settings displayed and edited by the GUI.
/// </summary>
/// <param name="RecordButton">Indicates whether the record button is engaged. When <paramref name="TriggerMode"/> is false this starts recording directly; when true it arms recording.</param>
/// <param name="TriggerMode">Indicates whether recording is started on a trigger (true) or manually (false).</param>
/// <param name="CompressVideo">Indicates whether recordings should be compressed.</param>
/// <param name="FileName">The file name template used to save Miniscope data.</param>
/// <param name="Suffix">The suffix appended to the file name when saving Miniscope data.</param>
/// <param name="RecordingDuration">The configured recording duration, in seconds.</param>
/// <param name="UseRecordDuration">Indicates whether recording should automatically stop after <paramref name="RecordingDuration"/> seconds.</param>
/// <param name="TriggerInput">Indicates which digital input is used to trigger recording.</param>
/// <param name="AutomaticRestart">Indicates whether a new recording should start automatically each time the recording duration elapses, until recording is manually stopped.</param>
public record FileSettings(bool RecordButton, bool TriggerMode, bool CompressVideo, string FileName, PathSuffix Suffix, int RecordingDuration, bool UseRecordDuration, MiniscopeDaqDigitalIn TriggerInput, bool AutomaticRestart);

/// <summary>
/// Combines individual file setting values into a single <see cref="FileSettings"/>.
/// </summary>
[Description("Combines individual file setting values into a single object.")]
[Combinator]
public class CreateFileSettings
{
    /// <summary>
    /// Creates a <see cref="FileSettings"/> by combining the latest values from each individual file setting sequence.
    /// </summary>
    /// <param name="recordButton">Indicates whether the record button is engaged (records when not triggered, arms when triggered).</param>
    /// <param name="triggerMode">Indicates whether recording is started on a trigger (true) or manually (false).</param>
    /// <param name="compressVideo">Indicates whether recordings should be compressed.</param>
    /// <param name="fileName">The file name template used to save Miniscope data.</param>
    /// <param name="suffix">The suffix appended to the file name when saving Miniscope data.</param>
    /// <param name="recordingDuration">The configured recording duration, in seconds.</param>
    /// <param name="useRecordDuration">Indicates whether recording should automatically stop after <paramref name="recordingDuration"/> seconds.</param>
    /// <param name="triggerInput">Indicates which digital input is used to trigger recording.</param>
    /// <param name="automaticRestart">Indicates whether a new recording should start automatically each time the recording duration elapses.</param>
    /// <returns>A sequence of <see cref="FileSettings"/> objects.</returns>
    public IObservable<FileSettings> Process(
        IObservable<bool> recordButton,
        IObservable<bool> triggerMode,
        IObservable<bool> compressVideo,
        IObservable<string> fileName,
        IObservable<PathSuffix> suffix,
        IObservable<int> recordingDuration,
        IObservable<bool> useRecordDuration,
        IObservable<MiniscopeDaqDigitalIn> triggerInput,
        IObservable<bool> automaticRestart)
    {
        return Observable.CombineLatest(
            recordButton,
            triggerMode,
            compressVideo,
            fileName,
            suffix,
            recordingDuration,
            useRecordDuration,
            triggerInput,
            automaticRestart,
            (recordButton, triggerMode, compressVideo, fileName, suffix, recordingDuration, useRecordDuration, triggerInput, automaticRestart) => new FileSettings(recordButton, triggerMode, compressVideo, fileName, suffix, recordingDuration, useRecordDuration, triggerInput, automaticRestart));
    }
}
