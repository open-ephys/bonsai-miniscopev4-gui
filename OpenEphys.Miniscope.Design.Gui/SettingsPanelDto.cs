using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

namespace OpenEphys.Miniscope.Design.Gui;

/// <summary>
/// Represents the combined settings state for all panels in the GUI.
/// </summary>
/// <param name="Miniscope">The Miniscope acquisition settings.</param>
/// <param name="File">The file saving settings.</param>
/// <param name="Saturation">The saturation overlay settings.</param>
/// <param name="Dff">The dF/F calculation settings.</param>
/// <param name="Commutator">The commutator connection and settings.</param>
public record SettingsPanelDto(MiniscopeSettingsDto Miniscope, FileSettingsDto File, SaturationSettingsDto Saturation, DffSettingsDto Dff, CommutatorSettingsDto Commutator);

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
    /// <param name="saturation">The saturation overlay settings.</param>
    /// <param name="dff">The dF/F calculation settings.</param>
    /// <param name="commutator">The commutator connection and settings.</param>
    /// <returns>A sequence of <see cref="SettingsPanelDto"/> objects.</returns>
    public IObservable<SettingsPanelDto> Process(
        IObservable<MiniscopeSettingsDto> miniscope,
        IObservable<FileSettingsDto> file,
        IObservable<SaturationSettingsDto> saturation,
        IObservable<DffSettingsDto> dff,
        IObservable<CommutatorSettingsDto> commutator)
    {
        return Observable.CombineLatest(
            miniscope,
            file,
            saturation,
            dff,
            commutator,
            (miniscope, file, saturation, dff, commutator) => new SettingsPanelDto(miniscope, file, saturation, dff, commutator));
    }
}
