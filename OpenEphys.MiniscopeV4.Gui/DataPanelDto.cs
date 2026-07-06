using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Represents the values the <see cref="DataPanel"/> both displays and edits: the rolling buffer
/// size (Time Series tab) and the saturation and dF/F settings now edited inside their image tabs.
/// </summary>
/// <param name="BufferSize">The rolling buffer size, in samples, used by the time series plots.</param>
/// <param name="Saturation">The saturation overlay settings edited in the Saturation tab.</param>
/// <param name="Dff">The dF/F calculation settings edited in the dF/F tab.</param>
public record DataPanelDto(int BufferSize, SaturationSettingsDto Saturation, DffSettingsDto Dff);

/// <summary>
/// Combines the individual data panel setting values into a single <see cref="DataPanelDto"/>.
/// </summary>
[Description("Combines the individual data panel setting values into a single object.")]
[Combinator]
public class CreateDataPanelDto
{
    /// <summary>
    /// Creates a <see cref="DataPanelDto"/> by combining the latest value from each source sequence.
    /// </summary>
    /// <param name="bufferSize">The rolling buffer size, in samples, used by the time series plots.</param>
    /// <param name="saturation">The saturation overlay settings edited in the Saturation tab.</param>
    /// <param name="dff">The dF/F calculation settings edited in the dF/F tab.</param>
    /// <returns>A sequence of <see cref="DataPanelDto"/> objects.</returns>
    public IObservable<DataPanelDto> Process(
        IObservable<int> bufferSize,
        IObservable<SaturationSettingsDto> saturation,
        IObservable<DffSettingsDto> dff)
    {
        return Observable.CombineLatest(
            bufferSize,
            saturation,
            dff,
            (bufferSize, saturation, dff) => new DataPanelDto(bufferSize, saturation, dff));
    }
}
