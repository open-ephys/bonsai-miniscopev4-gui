using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;
using Bonsai.IO;

namespace OpenEphys.Miniscope.Design.Gui;

/// <summary>
/// Represents the file saving settings displayed and edited by the GUI.
/// </summary>
/// <param name="RecordButton">Indicates whether the manual record button is engaged.</param>
/// <param name="RecordOnTriggerButton">Indicates whether recording is configured to start on a trigger.</param>
/// <param name="VideoCodec">The codec used to encode the saved video file.</param>
/// <param name="FileName">The file name template used to save Miniscope data.</param>
/// <param name="Suffix">The suffix appended to the file name when saving Miniscope data.</param>
/// <param name="RecordingDuration">The configured recording duration, in seconds.</param>
/// <param name="UseRecordDuration">Indicates whether recording should automatically stop after <paramref name="RecordingDuration"/> seconds.</param>
public record FileSettingsDto(bool RecordButton, bool RecordOnTriggerButton, string VideoCodec, string FileName, PathSuffix Suffix, int RecordingDuration, bool UseRecordDuration);

/// <summary>
/// Combines individual file setting values into a single <see cref="FileSettingsDto"/>.
/// </summary>
[Description("Combines individual file setting values into a single object.")]
public class CreateFileSettingsDto : Transform<Tuple<bool, bool, string, string, PathSuffix, int, bool>, FileSettingsDto>
{
    /// <summary>
    /// Creates a <see cref="FileSettingsDto"/> from a sequence of tuples containing the file settings.
    /// </summary>
    /// <param name="source">A sequence of tuples containing the file settings.</param>
    /// <returns>A sequence of <see cref="FileSettingsDto"/> objects.</returns>
    public override IObservable<FileSettingsDto> Process(IObservable<Tuple<bool, bool, string, string, PathSuffix, int, bool>> source)
    {
        return source.Select(value => new FileSettingsDto(value.Item1, value.Item2, value.Item3, value.Item4, value.Item5, value.Item6, value.Item7));
    }
}
