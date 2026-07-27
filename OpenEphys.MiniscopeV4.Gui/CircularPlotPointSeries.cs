using Bonsai.ImGui.Visualizers;
using Bonsai.ImPlot;
using Hexa.NET.ImPlot;
using System;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Provides an adapter over a circular buffer used for providing data for
/// interactive visualizations.
/// </summary>
/// <typeparam name="TSource">The type of the elements stored in the circular buffer.</typeparam>
public class CircularPlotPointSeries<TSource> : IPlotPointGetter, IPlotPointGetterSeries
{
    private readonly CircularBuffer<TSource> storage;
    private readonly NamedPlotPointGetter[] series;

    internal CircularPlotPointSeries(CircularBuffer<TSource> buffer, NamedPlotPointGetter[] getters)
    {
        storage = buffer;
        series = getters;
    }

    /// <inheritdoc/>
    public int Count => storage.Count;

    /// <summary>
    /// Gets the index of the start of the buffer.
    /// </summary>
    public int Start => storage.Start;

    /// <summary>
    /// Gets the index of the end of the buffer.
    /// </summary>
    public int End => storage.End;

    /// <inheritdoc/>
    public ReadOnlySpan<NamedPlotPointGetter> Series => series;

    ImPlotPointGetter IPlotPointGetter.Getter => series[0].Getter;
}
