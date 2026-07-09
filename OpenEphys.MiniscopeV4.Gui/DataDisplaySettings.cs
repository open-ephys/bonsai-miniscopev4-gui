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
/// <param name="MaxProjection">The max pixel-value projection settings edited in the Max Projection tab.</param>
public record DataDisplaySettings(int BufferSize, SaturationSettings Saturation, DffSettings Dff, MaxProjectionSettings MaxProjection);

/// <summary>
/// Combines the individual data panel setting values into a single <see cref="DataDisplaySettings"/>.
/// </summary>
[Description("Combines the individual data panel setting values into a single object.")]
[Combinator]
public class CreateDataDisplaySettings
{
    /// <summary>
    /// Creates a <see cref="DataDisplaySettings"/> by combining the latest value from each source sequence.
    /// </summary>
    /// <param name="bufferSize">The rolling buffer size, in samples, used by the time series plots.</param>
    /// <param name="saturation">The saturation overlay settings edited in the Saturation tab.</param>
    /// <param name="dff">The dF/F calculation settings edited in the dF/F tab.</param>
    /// <param name="maxProjection">The max projection settings in the Max Projection tab.</param>
    /// <returns>A sequence of <see cref="DataDisplaySettings"/> objects.</returns>
    public IObservable<DataDisplaySettings> Process(
        IObservable<int> bufferSize,
        IObservable<SaturationSettings> saturation,
        IObservable<DffSettings> dff,
        IObservable<MaxProjectionSettings> maxProjection)
    {
        return Observable.CombineLatest(
            bufferSize,
            saturation,
            dff,
            maxProjection,
            (bufferSize, saturation, dff, maxProjection) => new DataDisplaySettings(bufferSize, saturation, dff, maxProjection));
    }
}
