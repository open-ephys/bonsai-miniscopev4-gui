using Bonsai;
using Bonsai.ImGui.Visualizers;
using Bonsai.Vision;
using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;
using OpenCV.Net;
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
    /// receives the remaining fraction. Must be between 0 and 1. Only used to seed the draggable
    /// image/signal splitter the first time it renders; the splitter itself is not persisted.
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

    /// <summary>
    /// Gets or sets the average frame rate, in Hz, used to display the acquisition frame rate.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public double AverageFrameRate { get; set; }

    /// <summary>
    /// Gets or sets the frame number of the current frame.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public int FrameNumber { get; set; }

    /// <summary>
    /// Gets or sets the number of dropped frames since acquisition started.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public int DroppedFrames { get; set; }

    static float ControlColumnWidth => 220f * UiScale.Current;

    static readonly Vector2 fillAvailable = new(-1, -1);
    static readonly ImPlotFlags plotFlags = ImPlotFlags.NoMenus | ImPlotFlags.NoInputs | ImPlotFlags.NoTitle;
    static readonly string[] digitalInLabels = new string[] { MiniscopeDaqDigitalIn.DigitalIn0.ToString(), MiniscopeDaqDigitalIn.DigitalIn1.ToString() };
    static readonly string[] histogramAxisTickLabels = new string[] { "0%", "20%", "40%", "60%", "80%", "100%" };

    /// <summary>
    /// Renders the data panel for each source value and forwards the value unchanged.
    /// </summary>
    /// <param name="source">The sequence of values tied to the render tick of DearImGui.</param>
    /// <returns>The unmodified <paramref name="source"/> sequence.</returns>
    public unsafe IObservable<Tuple<TSource, DataDisplaySettings>> Process<TSource>(IObservable<Tuple<TSource, DataDisplaySettings>> source)
    {
        return Observable.Create<Tuple<TSource, DataDisplaySettings>>(observer =>
        {
            var sourceObserver = Observer.Create<Tuple<TSource, DataDisplaySettings>>(
                value =>
                {
                    var dataDisplaySettings = value.Item2;
                    var bufferSize = dataDisplaySettings.BufferSize;

                    int satThreshold = dataDisplaySettings.Saturation.Threshold;
                    var satColor = new Vector4(
                        (float)dataDisplaySettings.Saturation.Color.Val2 / 255,
                        (float)dataDisplaySettings.Saturation.Color.Val1 / 255,
                        (float)dataDisplaySettings.Saturation.Color.Val0 / 255,
                        (float)dataDisplaySettings.Saturation.Color.Val3 / 255);

                    int backgroundFrames = dataDisplaySettings.Dff.BackgroundFrames;
                    double backgroundThreshold = dataDisplaySettings.Dff.BackgroundThreshold;
                    int sigma = dataDisplaySettings.Dff.Sigma;

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
                        bool expanded = DataPanelLayout.ImageExpanded;
                        float totalHeight = ImGui.GetContentRegionAvail().Y;
                        float tabBarHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y;
                        float itemSpacingY = ImGui.GetStyle().ItemSpacing.Y;
                        float splitterReserve = DataPanelLayout.SplitterThickness + itemSpacingY * 2f;
                        float availableForPanes = Math.Max(0f, totalHeight - splitterReserve);

                        float imageChildHeight = expanded
                            ? totalHeight - tabBarHeight
                            : DataPanelLayout.GetImagePaneHeight(availableForPanes, Math.Max(0f, Math.Min(1f, ImageHeightFraction))) - tabBarHeight;

                        if (ImGui.BeginChild("##image_pane", new Vector2(-1, imageChildHeight), ImGuiChildFlags.None))
                        {
                            var availableSize = ImGui.GetContentRegionAvail();
                            availableSize.Y -= tabBarHeight;

                            float controlColumnWidth = ControlColumnWidth;
                            float imageAreaWidth = Math.Max(0f, availableSize.X - controlColumnWidth - ImGui.GetStyle().ItemSpacing.X);
                            float imageAreaHeight = Math.Max(0f, availableSize.Y);

                            var imageAreaSize = new Vector2(imageAreaWidth, imageAreaHeight);
                            var displaySize = CalculateDisplaySize(imageAreaSize, new Vector2(ImageWidth, ImageHeight));

                            if (ImGui.BeginTabBar("##ImageTabBar", ImGuiTabBarFlags.NoCloseWithMiddleMouseButton | ImGuiTabBarFlags.DrawSelectedOverline))
                            {
                                if (ImGui.BeginTabItem("Image##Image"))
                                {
                                    RenderImageArea("##image_area_raw", imageAreaSize, displaySize, MiniscopeImage);
                                    ImGui.SameLine();
                                    if (BeginControlColumn("##image_controls_raw", controlColumnWidth, imageAreaHeight))
                                    {
                                        ImGui.TextUnformatted("Frames per Second:");
                                        ImGui.Text($"{AverageFrameRate:F1}");
                                        ImGui.Spacing();

                                        ImGui.TextUnformatted("Frame Number:");
                                        ImGui.Text($"{FrameNumber}");
                                        ImGui.Spacing();

                                        ImGui.TextUnformatted("Dropped Frames:");
                                        if (DroppedFrames > 0)
                                        {
                                            using (Palette.PushColor(ImGuiCol.Text, Palette.RedHovered))
                                                ImGui.Text($"{DroppedFrames}");
                                        }
                                        else
                                        {
                                            ImGui.Text($"{DroppedFrames}");
                                        }

                                        RenderExpandCollapseButton(imageAreaHeight);
                                    }
                                    EndControlColumn();

                                    ImGui.EndTabItem();
                                }

                                if (ImGui.BeginTabItem("Saturation##Saturation"))
                                {
                                    RenderImageArea("##image_area_saturation", imageAreaSize, displaySize, SaturationImage);
                                    ImGui.SameLine();
                                    if (BeginControlColumn("##image_controls_saturation", controlColumnWidth, imageAreaHeight))
                                    {
                                        ImGui.TextUnformatted("Threshold:");
                                        ImGui.SetNextItemWidth(-1f);
                                        ImGui.SliderInt("##saturation_threshold", ref satThreshold, byte.MinValue, byte.MaxValue - 1, ImGuiSliderFlags.AlwaysClamp);
                                        ImGui.Spacing();

                                        ImGui.TextUnformatted("Color:");
                                        if (ImGui.ColorEdit4("##saturation_color", ref satColor, ImGuiColorEditFlags.Uint8 | ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoOptions | ImGuiColorEditFlags.NoInputs))
                                        {
                                            satColor.X = Math.Max(0f, Math.Min(1f, satColor.X));
                                            satColor.Y = Math.Max(0f, Math.Min(1f, satColor.Y));
                                            satColor.Z = Math.Max(0f, Math.Min(1f, satColor.Z));
                                        }

                                        RenderExpandCollapseButton(imageAreaHeight);
                                    }
                                    EndControlColumn();

                                    ImGui.EndTabItem();
                                }

                                if (ImGui.BeginTabItem("dF/F##dFF"))
                                {
                                    RenderImageArea("##image_area_dff", imageAreaSize, displaySize, DFFImage);
                                    ImGui.SameLine();
                                    if (BeginControlColumn("##image_controls_dff", controlColumnWidth, imageAreaHeight))
                                    {
                                        ImGui.TextUnformatted("Background frames:");
                                        ImGui.SetNextItemWidth(-1f);
                                        int backgroundFramesMin = 2, backgroundFramesMax = 1000;
                                        if (ImGui.InputInt("##background_frames", ref backgroundFrames))
                                            backgroundFrames = Math.Max(backgroundFramesMin, Math.Min(backgroundFramesMax, backgroundFrames));
                                        ImGui.Spacing();

                                        ImGui.TextUnformatted("Background thresh.:");
                                        ImGui.SetNextItemWidth(-1f);
                                        double bgThreshMin = 0, bgThreshMax = 255;
                                        ImGui.SliderScalar("##background_threshold", ImGuiDataType.Double, &backgroundThreshold, &bgThreshMin, &bgThreshMax, "%.1f", ImGuiSliderFlags.AlwaysClamp);
                                        ImGui.Spacing();

                                        ImGui.TextUnformatted("Sigma (px):");
                                        ImGui.SetNextItemWidth(-1f);
                                        if (ImGui.InputInt("##sigma", ref sigma))
                                            sigma = Math.Max(0, sigma);

                                        RenderExpandCollapseButton(imageAreaHeight);
                                    }
                                    EndControlColumn();

                                    ImGui.EndTabItem();
                                }

                                ImGui.EndTabBar();
                            }

                            ImGui.EndChild();
                        }

                        if (!expanded)
                        {
                            ImGui.InvisibleButton("##data_splitter", new Vector2(-1f, DataPanelLayout.SplitterThickness));

                            bool hovered = ImGui.IsItemHovered();
                            bool active = ImGui.IsItemActive();

                            if (active)
                                DataPanelLayout.Drag(ImGui.GetIO().MouseDelta.Y, availableForPanes);
                            if (hovered || active)
                                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);

                            var splitterDrawList = ImGui.GetWindowDrawList();
                            Vector2 splitterMin = ImGui.GetItemRectMin();
                            Vector2 splitterMax = ImGui.GetItemRectMax();
                            float splitterY = (splitterMin.Y + splitterMax.Y) * 0.5f;

                            uint splitterColor =
                                active ? ImGui.GetColorU32(ImGuiCol.SeparatorActive) :
                                hovered ? ImGui.GetColorU32(ImGuiCol.SeparatorHovered) :
                                          ImGui.GetColorU32(ImGuiCol.Separator);

                            float splitterThickness =
                                active ? 3.0f :
                                hovered ? 2.0f :
                                          1.0f;

                            splitterDrawList.AddLine(
                                new Vector2(splitterMin.X, splitterY),
                                new Vector2(splitterMax.X, splitterY),
                                splitterColor,
                                splitterThickness);

                            if (ImGui.BeginChild("##signal_pane", new Vector2(-1, -1), ImGuiChildFlags.None))
                            {
                                if (ImGui.BeginTabBar("##SignalTabBar"))
                                {
                                    if (ImGui.BeginTabItem("Quaternion"))
                                    {
                                        if (AcquisitionStatus)
                                        {
                                            ImGui.BeginDisabled();
                                        }

                                        var bufferInputWidth = 60f * UiScale.Current;
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

                                        if (QuaternionSeries == null && DigitalInSeries == null)
                                        {
                                            ImGui.Text("No data to display");
                                        }
                                        else if (ImPlot.BeginPlot("##quaternion_series", fillAvailable, plotFlags))
                                        {
                                            ImPlotAxisFlags axisFlags = ImPlotAxisFlags.AutoFit | ImPlotAxisFlags.NoMenus | ImPlotAxisFlags.NoTickMarks | ImPlotAxisFlags.NoGridLines | ImPlotAxisFlags.NoTickLabels;
                                            ImPlot.SetupAxes("", "", axisFlags, axisFlags);
                                            ImPlot.SetupAxisLimits(ImAxis.Y1, -1.05, 1.05, ImPlotCond.Always);

                                            if (QuaternionSeries != null)
                                            {
                                                for (int i = 0; i < QuaternionSeries.Series.Length; i++)
                                                {
                                                    var line = QuaternionSeries.Series[i];
                                                    ImPlot.PlotLineG(line.Name, line.Getter, null, QuaternionSeries.Count);
                                                }
                                            }

                                            if (DigitalInSeries != null)
                                            {
                                                // NB: Plot a dummy line to ensure the digital lines are plotted above the axis line.
                                                float* xs = stackalloc float[2] { 0, 1 };
                                                float* ys = stackalloc float[2] { 0, 0 };
                                                ImPlot.PushStyleVar(ImPlotStyleVar.LineWeight, 0.0f);
                                                ImPlot.PlotDigital("##dummy", xs, ys, 2);
                                                ImPlot.PopStyleVar();

                                                for (int i = 0; i < DigitalInSeries.Series.Length; i++)
                                                {
                                                    var line = DigitalInSeries.Series[i];
                                                    ImPlot.PlotDigitalG($"DigitalIn{i}", line.Getter, null, DigitalInSeries.Count);
                                                }
                                            }

                                            ImPlot.EndPlot();
                                        }

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
                        }

                        ImGui.EndChild();
                    }

                    var updatedDisplaySettings = new DataDisplaySettings(
                        bufferSize,
                        new SaturationSettings(satThreshold, new Scalar(satColor.Z * 255, satColor.Y * 255, satColor.X * 255, satColor.W * 255)),
                        new DffSettings(backgroundFrames, backgroundThreshold, sigma));

                    observer.OnNext(Tuple.Create(value.Item1, updatedDisplaySettings));
                },
                observer.OnError,
                observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }

    static void RenderImageArea(string id, Vector2 size, Vector2 displaySize, ImTextureRef image)
    {
        if (ImGui.BeginChild(id, size))
            PlotImage(displaySize, image);
        ImGui.EndChild();
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
    }

    static bool BeginControlColumn(string id, float width, float height) =>
        ImGui.BeginChild(id, new Vector2(width, height), ImGuiChildFlags.None);

    static void EndControlColumn() => ImGui.EndChild();

    static void RenderExpandCollapseButton(float columnHeight)
    {
        float buttonHeight = ImGui.GetFrameHeight() * 2f;
        float targetY = columnHeight - buttonHeight;
        if (targetY > ImGui.GetCursorPosY())
            ImGui.SetCursorPosY(targetY);

        if (ImGui.Button(DataPanelLayout.ImageExpanded ? "Collapse##image_expand_toggle" : "Expand##image_expand_toggle", new Vector2(-1f, buttonHeight)))
            DataPanelLayout.ImageExpanded = !DataPanelLayout.ImageExpanded;
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
