using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Represents the combined settings state for all panels in the GUI.
/// </summary>
/// <param name="Miniscope">The Miniscope acquisition settings.</param>
/// <param name="Commutator">The commutator connection and settings.</param>
public record HardwareSettings(MiniscopeSettings Miniscope, CommutatorSettings Commutator);

/// <summary>
/// Combines individual settings DTOs into a single <see cref="HardwareSettings"/>.
/// </summary>
[Description("Combines all settings DTOs into a single object for use with the SettingsPanel combinator.")]
[Combinator]
public class CreateHardwareSettings
{
    /// <summary>
    /// Creates a <see cref="HardwareSettings"/> by combining the latest value from each settings DTO sequence.
    /// </summary>
    /// <param name="miniscope">The Miniscope acquisition settings.</param>
    /// <param name="commutator">The commutator connection and settings.</param>
    /// <returns>A sequence of <see cref="HardwareSettings"/> objects.</returns>
    public IObservable<HardwareSettings> Process(
        IObservable<MiniscopeSettings> miniscope,
        IObservable<CommutatorSettings> commutator)
    {
        return Observable.CombineLatest(
            miniscope,
            commutator,
            (miniscope, commutator) => new HardwareSettings(miniscope, commutator));
    }
}
