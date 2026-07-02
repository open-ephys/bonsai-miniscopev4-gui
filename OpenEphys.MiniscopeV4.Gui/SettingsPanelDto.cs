using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Represents the combined settings state for all panels in the GUI.
/// </summary>
/// <param name="Miniscope">The Miniscope acquisition settings.</param>
/// <param name="File">The file saving settings.</param>
/// <param name="Commutator">The commutator connection and settings.</param>
public record SettingsPanelDto(MiniscopeSettingsDto Miniscope, FileSettingsDto File, CommutatorSettingsDto Commutator);

/// <summary>
/// Combines individual settings DTOs into a single <see cref="SettingsPanelDto"/>.
/// </summary>
[Description("Combines all settings DTOs into a single object for use with the SettingsPanel combinator.")]
[Combinator]
public class CreateSettingsPanelDto
{
    /// <summary>
    /// Creates a <see cref="SettingsPanelDto"/> by combining the latest value from each settings DTO sequence.
    /// </summary>
    /// <param name="miniscope">The Miniscope acquisition settings.</param>
    /// <param name="file">The file saving settings.</param>
    /// <param name="commutator">The commutator connection and settings.</param>
    /// <returns>A sequence of <see cref="SettingsPanelDto"/> objects.</returns>
    public IObservable<SettingsPanelDto> Process(
        IObservable<MiniscopeSettingsDto> miniscope,
        IObservable<FileSettingsDto> file,
        IObservable<CommutatorSettingsDto> commutator)
    {
        return Observable.CombineLatest(
            miniscope,
            file,
            commutator,
            (miniscope, file, commutator) => new SettingsPanelDto(miniscope, file, commutator));
    }
}
