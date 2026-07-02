using Bonsai;
using Bonsai.ImGui.Visualizers;
using Bonsai.Vision;
using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;
using OpenEphys.Miniscope;
using System;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Xml.Serialization;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Renders the image tabs and signal tabs inside a single child region that fills the available content area.
/// </summary>
[Combinator]
[Description("Renders the image and signal plot panels inside a single child region.")]
public class DataPanel
{
    /// <summary>
    /// Gets or sets the texture displayed in the raw image tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public ImTextureRef MiniscopeImage { get; set; }

    /// <summary>
    /// Gets or sets the texture displayed in the saturation tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public ImTextureRef SaturationImage { get; set; }

    /// <summary>
    /// Gets or sets the texture displayed in the dF/F tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public ImTextureRef DFFImage { get; set; }

    /// <summary>
    /// Gets or sets the height, in pixels, of the source images used to calculate the display size.
    /// </summary>
    public int ImageHeight { get; set; } = 100;

    /// <summary>
    /// Gets or sets the width, in pixels, of the source images used to calculate the display size.
    /// </summary>
    public int ImageWidth { get; set; } = 100;

    /// <summary>
    /// Gets or sets the fraction of the available height allocated to the image pane. The signal pane
    /// receives the remaining fraction. Must be between 0 and 1.
    /// </summary>
    public float ImageHeightFraction { get; set; } = 0.5f;

    /// <summary>
    /// Gets or sets the rolling series of quaternion orientation values plotted in the time series tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public RollingPlotPointSeries<Quaternion> QuaternionSeries { get; set; }

    /// <summary>
    /// Gets or sets the rolling series of digital input values plotted in the time series tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public RollingPlotPointSeries<Tuple<bool, bool>> DigitalInSeries { get; set; }

    /// <summary>
    /// Gets or sets the pixel intensity histogram plotted in the histogram tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public ScalarHistogram ImageHistogram { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether acquisition is currently in progress.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public bool AcquisitionStatus { get; set; }

    static readonly Vector2 fillAvailable = new(-1, -1);
    static readonly ImPlotFlags plotFlags = ImPlotFlags.NoMenus | ImPlotFlags.NoInputs | ImPlotFlags.NoTitle;
    static readonly string[] digitalInLabels = new string[] { MiniscopeDaqDigitalIn.DigitalIn0.ToString(), MiniscopeDaqDigitalIn.DigitalIn1.ToString() };
    static readonly string[] histogramAxisTickLabels = new string[] { "0%", "20%", "40%", "60%", "80%", "100%" };

    /// <summary>
    /// Renders the data panel for each source value and forwards the value unchanged.
    /// </summary>
    /// <param name="source">The sequence of values tied to the render tick of DearImGui.</param>
    /// <returns>The unmodified <paramref name="source"/> sequence.</returns>
    public unsafe IObservable<Tuple<TSource, int>> Process<TSource>(IObservable<Tuple<TSource, int>> source)
    {
        return Observable.Create<Tuple<TSource, int>>(observer =>
        {
            var sourceObserver = Observer.Create<Tuple<TSource, int>>(
                value =>
                {
                    var bufferSize = value.Item2;

                    float totalHeight = ImGui.GetContentRegionAvail().Y;
                    float fraction = Math.Max(0f, Math.Min(1f, ImageHeightFraction));
                    float tabBarHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y;
                    float imageChildHeight = totalHeight * fraction - tabBarHeight;

                    if (!AcquisitionStatus)
                    {
                        if (!MiniscopeImage.TexID.IsNull)
                        {
                            MiniscopeImage = default;
                        }

                        if (!SaturationImage.TexID.IsNull)
                        {
                            SaturationImage = default;
                        }

                        if (!DFFImage.TexID.IsNull)
                        {
                            DFFImage = default;
                        }
                    }

                    float consoleReserve = ConsoleLayout.ReservedHeight(ImGui.GetStyle().ItemSpacing.Y);
                    if (ImGui.BeginChild("##Data", new Vector2(-1f, -consoleReserve)))
                    {
                        if (ImGui.BeginChild("##image_pane", new Vector2(-1, imageChildHeight), ImGuiChildFlags.None))
                        {
                            var availableSize = ImGui.GetContentRegionAvail();
                            availableSize.Y -= tabBarHeight;

                            var displaySize = CalculateDisplaySize(
                                availableSize,
                                new Vector2(ImageWidth, ImageHeight));

                            if (ImGui.BeginTabBar("##ImageTabBar", ImGuiTabBarFlags.NoCloseWithMiddleMouseButton | ImGuiTabBarFlags.DrawSelectedOverline))
                            {
                                if (ImGui.BeginTabItem("Image##Image"))
                                {
                                    PlotImage(displaySize, MiniscopeImage);
                                    ImGui.EndTabItem();
                                }

                                if (ImGui.BeginTabItem("Saturation##Saturation"))
                                {
                                    PlotImage(displaySize, SaturationImage);
                                    ImGui.EndTabItem();
                                }

                                if (ImGui.BeginTabItem("dF/F##dFF"))
                                {
                                    PlotImage(displaySize, DFFImage);
                                    ImGui.EndTabItem();
                                }

                                ImGui.EndTabBar();
                            }

                            ImGui.EndChild();
                        }

                        if (ImGui.BeginChild("##signal_pane", new Vector2(-1, -1), ImGuiChildFlags.None))
                        {
                            if (ImGui.BeginTabBar("##SignalTabBar"))
                            {
                                if (ImGui.BeginTabItem("Time Series"))
                                {
                                    if (AcquisitionStatus)
                                    {
                                        ImGui.BeginDisabled();
                                    }

                                    var bufferInputWidth = 60f;
                                    ImGui.AlignTextToFramePadding();
                                    ImGui.Text("Buffer Size: ");
                                    ImGui.SameLine();
                                    ImGui.SetNextItemWidth(bufferInputWidth);
                                    if (ImGui.InputInt("##statusbar_buffersize", ref bufferSize, 0, 0))
                                    {
                                        bufferSize = Math.Max(2, bufferSize);
                                    }

                                    if (AcquisitionStatus)
                                    {
                                        ImGui.EndDisabled();
                                    }

                                    ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));
                                    int numRows = 3, numCols = 1;
                                    float* rowRatios = stackalloc float[3] { 2f, 1f, 1f };

                                    if (QuaternionSeries == null && DigitalInSeries == null)
                                    {
                                        ImGui.Text("No data to display");
                                    }
                                    else if (ImPlot.BeginSubplots("##time_series_subplots", numRows, numCols, fillAvailable, ImPlotSubplotFlags.LinkAllX |ImPlotSubplotFlags.NoResize, rowRatios, null))
                                    {
                                        ImPlotAxisFlags axisFlags = ImPlotAxisFlags.AutoFit | ImPlotAxisFlags.NoMenus | ImPlotAxisFlags.NoTickMarks | ImPlotAxisFlags.NoGridLines | ImPlotAxisFlags.NoTickLabels;

                                        if (ImPlot.BeginPlot("##quaternion_series", fillAvailable, plotFlags))
                                        {
                                            ImPlot.SetupAxes("", "Quaternion", axisFlags, axisFlags);
                                            ImPlot.SetupAxisLimits(ImAxis.Y1, -1.05, 1.05, ImPlotCond.Always);

                                            if (QuaternionSeries != null)
                                            {
                                                for (int i = 0; i < QuaternionSeries.Series.Length; i++)
                                                {
                                                    var line = QuaternionSeries.Series[i];
                                                    ImPlot.PlotLineG(line.Name, line.Getter, null, QuaternionSeries.Count);
                                                }
                                            }

                                            ImPlot.EndPlot();
                                        }

                                        if (ImPlot.BeginPlot("##digital_in1_series", fillAvailable, plotFlags | ImPlotFlags.NoLegend))
                                        {
                                            ImPlot.SetNextLineStyle(ImPlot.GetColormapColor(4));
                                            PlotDigitalSeries(DigitalInSeries, 1, axisFlags);
                                            ImPlot.EndPlot();
                                        }

                                        if (ImPlot.BeginPlot("##digital_in0_series", fillAvailable, plotFlags | ImPlotFlags.NoLegend))
                                        {
                                            PlotDigitalSeries(DigitalInSeries, 0, axisFlags);
                                            ImPlot.EndPlot();
                                        }

                                        ImPlot.EndSubplots();
                                    }

                                    ImGui.PopStyleVar();

                                    ImGui.EndTabItem();
                                }

                                if (ImGui.BeginTabItem("Histogram"))
                                {
                                    if (ImageHistogram == null)
                                    {
                                        ImGui.Text("No data to display");
                                    }
                                    else
                                    {
                                        const int binCount = 256;

                                        ImPlotAxisFlags flagsX = ImPlotAxisFlags.NoLabel | ImPlotAxisFlags.NoTickMarks | ImPlotAxisFlags.NoGridLines;
                                        ImPlotAxisFlags flagsY = ImPlotAxisFlags.AutoFit | ImPlotAxisFlags.NoTickLabels | ImPlotAxisFlags.NoTickMarks | ImPlotAxisFlags.NoGridLines;

                                        var hist = ImageHistogram.Val0;

                                        float[] bins = new float[binCount];
                                        for (int i = 0; i < binCount; i++)
                                            bins[i] = (float)hist.QueryValue(i);

                                        float max = bins.Max();
                                        if (max > 0f)
                                            for (int i = 0; i < binCount; i++)
                                                bins[i] /= max;

                                        if (ImPlot.BeginPlot("##histogram", fillAvailable, plotFlags))
                                        {
                                            double minValue = 0, maxValue = byte.MaxValue, axisOffset = 5;
                                            int numLabels = histogramAxisTickLabels.Length;


                                            ImPlot.SetupAxes("", "", flagsX, flagsY);
                                            ImPlot.SetupAxisLimits(ImAxis.X1, minValue - axisOffset, maxValue + axisOffset, ImPlotCond.Always);
                                            ImPlot.SetupAxisTicks(ImAxis.X1, minValue, maxValue, numLabels, histogramAxisTickLabels, false);

                                            fixed (float* binPtr = bins)
                                            {
                                                ImPlot.PlotBars("##pixel_intensity", binPtr, hist.Bins.GetDimSize(0), 2.0f);
                                            }

                                            ImPlot.EndPlot();
                                        }
                                    }

                                    ImGui.EndTabItem();
                                }

                                ImGui.EndTabBar();
                            }

                            ImGui.EndChild();
                        }

                        ImGui.EndChild();
                    }

                    observer.OnNext(Tuple.Create(value.Item1, bufferSize));
                },
                observer.OnError,
                observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }

    static void PlotImage(Vector2 displaySize, ImTextureRef image)
    {
        if (!image.TexID.IsNull)
        {
            float availableWidth = ImGui.GetContentRegionAvail().X;
            float offsetX = (availableWidth - displaySize.X) * 0.5f;
            if (offsetX > 0)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);
            ImGui.Image(image, displaySize);
        }
        else
            ImGui.Text("No image data found.");
    }

    static Vector2 CalculateDisplaySize(Vector2 availableRegion, Vector2 imageSize)
    {
        if (imageSize.X == 0 && imageSize.Y == 0)
            return Vector2.Zero;

        float displayWidth = availableRegion.X;
        float displayHeight = displayWidth * imageSize.Y / imageSize.X;
        if (displayHeight > availableRegion.Y)
        {
            displayHeight = availableRegion.Y;
            displayWidth = displayHeight * imageSize.X / imageSize.Y;
        }

        return new Vector2(displayWidth, displayHeight);
    }

    static unsafe void PlotDigitalSeries(RollingPlotPointSeries<Tuple<bool, bool>> series, int index, ImPlotAxisFlags axisFlags)
    {
        ImPlot.SetupAxes("", $"DigitalIn{index}", axisFlags, axisFlags);
        ImPlot.SetupAxisLimits(ImAxis.Y1, -0.05, 1.05, ImPlotCond.Always);

        if (series != null)
        {
            var line = series.Series[index];
            ImPlot.PlotStairsG(digitalInLabels[index], line.Getter, null, series.Count);
        }
    }
}
