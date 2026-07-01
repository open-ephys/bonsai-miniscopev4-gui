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
/// <param name="RecordButton">Indicates whether the manual record button is engaged.</param>
/// <param name="RecordOnTriggerButton">Indicates whether recording is configured to start on a trigger.</param>
/// <param name="CompressVideo">Indicates whether recordings should be compressed.</param>
/// <param name="FileName">The file name template used to save Miniscope data.</param>
/// <param name="Suffix">The suffix appended to the file name when saving Miniscope data.</param>
/// <param name="RecordingDuration">The configured recording duration, in seconds.</param>
/// <param name="UseRecordDuration">Indicates whether recording should automatically stop after <paramref name="RecordingDuration"/> seconds.</param>
/// <param name="TriggerInput">Indicates which digital input is used to trigger recording.</param>
public record FileSettingsDto(bool RecordButton, bool RecordOnTriggerButton, bool CompressVideo, string FileName, PathSuffix Suffix, int RecordingDuration, bool UseRecordDuration, MiniscopeDaqDigitalIn TriggerInput);

/// <summary>
/// Combines individual file setting values into a single <see cref="FileSettingsDto"/>.
/// </summary>
[Description("Combines individual file setting values into a single object.")]
[Combinator]
public class CreateFileSettingsDto
{
    /// <summary>
    /// Creates a <see cref="FileSettingsDto"/> by combining the latest values from each individual file setting sequence.
    /// </summary>
    /// <param name="recordButton">Indicates whether the manual record button is engaged.</param>
    /// <param name="recordOnTriggerButton">Indicates whether recording is configured to start on a trigger.</param>
    /// <param name="compressVideo">Indicates whether recordings should be compressed.</param>
    /// <param name="fileName">The file name template used to save Miniscope data.</param>
    /// <param name="suffix">The suffix appended to the file name when saving Miniscope data.</param>
    /// <param name="recordingDuration">The configured recording duration, in seconds.</param>
    /// <param name="useRecordDuration">Indicates whether recording should automatically stop after <paramref name="recordingDuration"/> seconds.</param>
    /// <param name="triggerInput">Indicates which digital input is used to trigger recording.</param>
    /// <returns>A sequence of <see cref="FileSettingsDto"/> objects.</returns>
    public IObservable<FileSettingsDto> Process(
        IObservable<bool> recordButton,
        IObservable<bool> recordOnTriggerButton,
        IObservable<bool> compressVideo,
        IObservable<string> fileName,
        IObservable<PathSuffix> suffix,
        IObservable<int> recordingDuration,
        IObservable<bool> useRecordDuration,
        IObservable<MiniscopeDaqDigitalIn> triggerInput)
    {
        return Observable.CombineLatest(
            recordButton,
            recordOnTriggerButton,
            compressVideo,
            fileName,
            suffix,
            recordingDuration,
            useRecordDuration,
            triggerInput,
            (recordButton, recordOnTriggerButton, compressVideo, fileName, suffix, recordingDuration, useRecordDuration, triggerInput) => new FileSettingsDto(recordButton, recordOnTriggerButton, compressVideo, fileName, suffix, recordingDuration, useRecordDuration, triggerInput));
    }
}
