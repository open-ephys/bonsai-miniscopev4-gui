using Bonsai;
using Bonsai.Vision;
using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;
using OpenCV.Net;
using OpenEphys.Miniscope;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Identifies the image tab currently selected in the <see cref="DataPanel"/>.
/// </summary>
public enum ImageTab
{
    /// <summary> No image tab is selected.</summary>
    None,

    /// <summary>The raw image tab.</summary>
    Raw,

    /// <summary>The saturation overlay tab.</summary>
    Saturation,

    /// <summary>The dF/F (delta-F over F) tab.</summary>
    Dff,

    /// <summary>The max pixel-value projection tab.</summary>
    MaxProjection,

    /// <summary>The reference-image overlay tab.</summary>
    Overlay,
}

/// <summary>
/// Renders the image tabs and signal tabs inside a single child region that fills the available content area.
/// </summary>
[Combinator]
[Description("Renders the image and signal plot panels inside a single child region.")]
public class DataPanel
{
    /// <summary>
    /// Gets or sets the texture displayed in the active image tab. The workflow gates the image
    /// pipelines by the emitted <see cref="ImageTab"/> and feeds back only the active tab's texture.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public ImTextureRef ActiveImage { get; set; }

    /// <summary>
    /// Gets or sets the height, in pixels, of the source images used to calculate the display size.
    /// </summary>
    public int ImageHeight { get; set; } = 100;

    /// <summary>
    /// Gets or sets the width, in pixels, of the source images used to calculate the display size.
    /// </summary>
    public int ImageWidth { get; set; } = 100;

    /// <summary>
    /// Gets or sets the circular buffer of quaternion orientation values plotted in the time series tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public CircularPlotPointSeries<Quaternion> QuaternionSeries { get; set; }

    /// <summary>
    /// Gets or sets the circular buffer of digital input values plotted in the time series tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public CircularPlotPointSeries<Tuple<bool, bool>> DigitalInSeries { get; set; }

    /// <summary>
    /// Gets or sets the circular buffer of Euler angle values plotted in the time series tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public CircularPlotPointSeries<TaitBryanAngles> EulerAnglesSeries { get; set; }

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
    /// Gets or sets a value indicating whether the data display is paused.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public bool Paused { get; set; }

    /// <summary>
    /// Gets or sets the average frame rate, in Hz, used to display the acquisition frame rate.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public double AverageFrameRate { get; set; }

    /// <summary>
    /// Gets or sets the selected <see cref="FrameRateV4"/>.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public FrameRateV4 SelectedFrameRate { get; set; }

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

    /// <summary>
    /// Gets or sets the data path set by <see cref="FilePanel"/>.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public string DataPath { get; set; }

    static float ControlColumnWidth => 220f * UiScale.Current;

    const float BaseMinImagePaneHeight = 150f;
    const float BaseMinSignalPaneHeight = 120f;

    float MinImagePaneHeight => BaseMinImagePaneHeight * UiScale.Current;
    float MinSignalPaneHeight => BaseMinSignalPaneHeight * UiScale.Current;

    /// <summary>Thickness, in pixels, of the draggable splitter between the image and signal panes.</summary>
    float ImageSplitterThickness => 6f * UiScale.Current;

    const float DefaultImagePaneHeightFraction = 0.7f;

    /// <summary>
    /// Clamps the image pane height so it never shrinks below its minimum height given
    /// <paramref name="availableForPanes"/>.
    /// </summary>
    float ClampImagePaneHeight(float height, float availableForPanes)
    {
        if (height < 0)
            height  = availableForPanes * DefaultImagePaneHeightFraction;

        float maxHeight = Math.Max(MinImagePaneHeight, availableForPanes - MinSignalPaneHeight);
        return Math.Max(MinImagePaneHeight, Math.Min(maxHeight, height));
    }

    static Vector4 ConvertScalarColorToVector4(Scalar color) => new((float)color.Val2 / 255, (float)color.Val1 / 255, (float)color.Val0 / 255, (float)color.Val3 / 255);

    static Scalar ConvertVector4ColorToScalar(Vector4 color) => new(color.Z * 255, color.Y * 255, color.X * 255, color.W * 255);

    static Vector4 ClampVector4Color(Vector4 color)
    {
        color.X = Math.Max(0f, Math.Min(1f, color.X));
        color.Y = Math.Max(0f, Math.Min(1f, color.Y));
        color.Z = Math.Max(0f, Math.Min(1f, color.Z));
        return color;
    }

    static readonly Vector2 fillAvailable = new(-1, -1);
    static readonly ImPlotFlags plotFlags = ImPlotFlags.NoMenus | ImPlotFlags.NoInputs | ImPlotFlags.NoTitle | ImPlotFlags.NoLegend;
    static readonly string[] digitalInLabels = new string[] { MiniscopeDaqDigitalIn.DigitalIn0.ToString(), MiniscopeDaqDigitalIn.DigitalIn1.ToString() };

    static readonly PlotLegend quaternionLegend = new(
        "quaternion",
        new PlotLegend.Entry("X", Palette.LineColor(0)),
        new PlotLegend.Entry("Y", Palette.LineColor(1)),
        new PlotLegend.Entry("Z", Palette.LineColor(2)),
        new PlotLegend.Entry("W", Palette.LineColor(3)));
    static readonly PlotLegend digitalInLegend = new(
        "digitalin",
        new PlotLegend.Entry(digitalInLabels[0], Palette.LineColor(4)),
        new PlotLegend.Entry(digitalInLabels[1], Palette.LineColor(5)));
    static readonly PlotLegend eulerAngleLegend = new(
        "euler_angles",
        new PlotLegend.Entry("Yaw", Palette.LineColor(0)),
        new PlotLegend.Entry("Pitch", Palette.LineColor(1)),
        new PlotLegend.Entry("Roll", Palette.LineColor(2)));

    static readonly double[] eulerGridStepCandidates = { 5, 10, 15, 20, 30, 45, 60, 90, 180 };
    const float MinGridPixelSpacing = 32f;

    static readonly double[] timeGridStepCandidates = { 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60 };
    const float MinTimeGridPixelSpacing = 48f;
    const float MinTimeLabelGap = 8f;

    const double TimeLabelFraction = 0.15;

    static readonly ImPlotSubplotFlags signalSubplotFlags = ImPlotSubplotFlags.NoTitle | ImPlotSubplotFlags.NoMenus | ImPlotSubplotFlags.NoResize | ImPlotSubplotFlags.NoLegend | ImPlotSubplotFlags.LinkAllX;

    const double DigitalAmplitude = 0.8;
    const double DigitalAxisMargin = 0.1;

    static readonly double[] TimebasePresetsSeconds = { 1, 2, 3, 4, 5, 10, 20, 30, 40, 50, 60, 72, 90, 120, 180 };

    static int GetNumberOfSamples(FrameRateV4 frameRate, double seconds) => (int)(seconds * ConvertFrameRateV4ToFps(frameRate) ?? 0);

    static IEnumerable<double> ClippedTimebasePresets(FrameRateV4 frameRate, int bufferCapacity, double currentTimebase)
    {
        var presets = TimebasePresetsSeconds.Where(seconds => GetNumberOfSamples(frameRate, seconds) <= bufferCapacity);

        if (currentTimebase > 0 && GetNumberOfSamples(frameRate, currentTimebase) <= bufferCapacity && !TimebasePresetsSeconds.Contains(currentTimebase))
            presets = presets.Append(currentTimebase);

        return presets.OrderBy(seconds => seconds);
    }

    static string FormatTimebase(double seconds) => $"{seconds:F1} s";

    static double? ConvertFrameRateV4ToFps(FrameRateV4 frameRate) => double.TryParse(frameRate.ToString().Substring(3, 2), out double result) ? result : null;

    static (double xMin, double xMax)? GetXAxisLimits(bool scrollable, ref bool wasScrollable, in PlotWindow window)
    {
        if (scrollable && wasScrollable)
            return null;

        wasScrollable = scrollable;
        return (0, window.XAxisMax);
    }

    static void SyncTimebaseWithAxisZoom(FrameRateV4 frameRate, bool scrollable, int bufferCapacity, ref double selectedTimebase)
    {
        if (!scrollable)
            return;

        var fps = ConvertFrameRateV4ToFps(frameRate) ?? 0;
        if (fps == 0)
            return;

        var limits = ImPlot.GetPlotLimits();
        double axisSampleCount = Math.Min((limits.X.Max - limits.X.Min) + 1, bufferCapacity);

        if (axisSampleCount == GetNumberOfSamples(frameRate, selectedTimebase))
            return;

        selectedTimebase = Math.Round(axisSampleCount / fps, 1);
    }

    static void IncrementTimebase(FrameRateV4 frameRate, int bufferCapacity, ref double selectedTimebase)
    {
        var options = ClippedTimebasePresets(frameRate, bufferCapacity, selectedTimebase).ToArray();
        if (options.Length == 0)
            options = new[] { TimebasePresetsSeconds[0] };
        int currentIndex = Array.IndexOf(options, selectedTimebase);
        if (currentIndex < 0)
            currentIndex = options.Length - 1;
        int nextIndex = Math.Min(currentIndex + 1, options.Length - 1);
        selectedTimebase = options[nextIndex];
    }

    static void DecrementTimebase(FrameRateV4 frameRate, int bufferCapacity, ref double selectedTimebase)
    {
        var options = ClippedTimebasePresets(frameRate, bufferCapacity, selectedTimebase).ToArray();
        if (options.Length == 0)
            options = new[] { TimebasePresetsSeconds[0] };
        int currentIndex = Array.IndexOf(options, selectedTimebase);
        if (currentIndex < 0)
            currentIndex = options.Length - 1;
        int nextIndex = Math.Max(currentIndex - 1, 0);
        selectedTimebase = options[nextIndex];
    }

    static void TimebaseControl(FrameRateV4 frameRate, int bufferCapacity, ref double selectedTimebase)
    {
        var options = ClippedTimebasePresets(frameRate, bufferCapacity, selectedTimebase).ToArray();
        if (options.Length == 0)
            options = new[] { TimebasePresetsSeconds[0] };
        if (Array.IndexOf(options, selectedTimebase) < 0)
            selectedTimebase = options[options.Length - 1];

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Timebase: ");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(70f * UiScale.Current);
        if (ImGui.BeginCombo("##timebase", FormatTimebase(selectedTimebase)))
        {
            foreach (var option in options)
            {
                bool selected = option == selectedTimebase;
                if (ImGui.Selectable(FormatTimebase(option), selected))
                    selectedTimebase = option;
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        if (!ImGui.GetIO().WantTextInput && (ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift)) && ImGui.GetIO().MouseWheel != 0.0f)
        {
            var scrollDelta = ImGui.GetIO().MouseWheel;

            if (scrollDelta > 0.0f)
            {
                DecrementTimebase(frameRate, bufferCapacity, ref selectedTimebase);
            }
            else
            {
                IncrementTimebase(frameRate, bufferCapacity, ref selectedTimebase);
            }
        }

        if (Tooltip.Begin())
        {
            Tooltip.AddLine("The timebase controls the length of data shown in the time-series plots when the display is not paused.");
            Tooltip.AddLine("When the display is paused, time-series data can be scrolled by clicking-and-\n" +
                "dragging, and zoomed in or out by using the mouse wheel.");
            Tooltip.AddKeyboardShortcut("Shift + Mouse Wheel");
            Tooltip.End();
        }
    }

    static Vector4 GridLineColor => new(0.5f, 0.5f, 0.5f, 0.35f);

    static Vector4 GridLabelColor()
    {
        var color = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        color.W *= 0.45f;
        return color;
    }

    static string FormatDegreeLabel(double value) => $"{value:0}°";

    static double ChooseGridStep(double axisSpan, float pixelSpan, double[] candidates, float minPixelSpacing)
    {
        foreach (var step in candidates)
        {
            float pixelsPerStep = (float)(step / axisSpan * pixelSpan);
            if (pixelsPerStep >= minPixelSpacing)
                return step;
        }

        return candidates[candidates.Length - 1];
    }

    static void DrawPlotGrid(double axisMin, double axisMax, double step, Func<double, string> labelFormatter)
    {
        if (step <= 0)
            return;

        var plotPos = ImPlot.GetPlotPos();
        var plotSize = ImPlot.GetPlotSize();
        var drawList = ImPlot.GetPlotDrawList();
        uint lineColor = ImGui.ColorConvertFloat4ToU32(GridLineColor);
        uint labelColor = ImGui.ColorConvertFloat4ToU32(GridLabelColor());
        float textHeight = ImGui.GetTextLineHeight();

        ImPlot.PushPlotClipRect();

        double start = Math.Ceiling(axisMin / step) * step;
        for (double level = start; level <= axisMax; level += step)
        {
            float y = ImPlot.PlotToPixels(axisMin, level).Y;
            drawList.AddLine(new Vector2(plotPos.X, y), new Vector2(plotPos.X + plotSize.X, y), lineColor);

            if (labelFormatter != null)
            {
                var textPos = new Vector2(plotPos.X + 4f, y - textHeight - 1f);
                drawList.AddText(textPos, labelColor, labelFormatter(level));
            }
        }

        ImPlot.PopPlotClipRect();
    }

    static string FormatTimeLabel(double seconds, double step) => step < 1 ? $"{seconds:0.0} s" : $"{seconds:0} s";

    static double TimeLabelLimit(double axisMin, double axisMax) => axisMin - (axisMax - axisMin) * TimeLabelFraction;

    static void DrawTimeGrid(double fps, double yAxisDataMin)
    {
        if (fps <= 0)
            return;

        var limits = ImPlot.GetPlotLimits();
        double minSeconds = limits.X.Min / fps;
        double maxSeconds = limits.X.Max / fps;
        if (maxSeconds <= minSeconds)
            return;

        var plotPos = ImPlot.GetPlotPos();
        var plotSize = ImPlot.GetPlotSize();
        double step = ChooseGridStep(maxSeconds - minSeconds, plotSize.X, timeGridStepCandidates, MinTimeGridPixelSpacing * UiScale.Current);
        if (step <= 0)
            return;

        var drawList = ImPlot.GetPlotDrawList();
        uint lineColor = ImGui.ColorConvertFloat4ToU32(GridLineColor);
        uint labelColor = ImGui.ColorConvertFloat4ToU32(GridLabelColor());
        float axisLabelY = ImPlot.PlotToPixels(limits.X.Min, yAxisDataMin).Y;

        float labelY = Math.Min(axisLabelY + 2f * UiScale.Current, plotPos.Y + plotSize.Y - ImGui.GetTextLineHeight());
        float edgePadding = 3f * UiScale.Current;
        float minLabelGap = MinTimeLabelGap * UiScale.Current;
        float labelLeftLimit = plotPos.X + edgePadding;
        float labelRightLimit = plotPos.X + plotSize.X - edgePadding;
        float lastLabelRight = float.NegativeInfinity;

        ImPlot.PushPlotClipRect();

        for (long index = (long)Math.Ceiling(minSeconds / step); ; index++)
        {
            double seconds = index * step;
            if (seconds > maxSeconds)
                break;

            float x = ImPlot.PlotToPixels(seconds * fps, yAxisDataMin).X;
            drawList.AddLine(new Vector2(x, plotPos.Y), new Vector2(x, axisLabelY), lineColor);

            var label = FormatTimeLabel(seconds, step);
            float textWidth = ImGui.CalcTextSize(label).X;
            float textX = Math.Max(labelLeftLimit, Math.Min(x - textWidth * 0.5f, labelRightLimit - textWidth));

            if (textX < lastLabelRight + minLabelGap)
                continue;

            drawList.AddText(new Vector2(textX, labelY), labelColor, label);
            lastLabelRight = textX + textWidth;
        }

        ImPlot.PopPlotClipRect();
    }

    /// <summary>
    /// Renders the data panel and returns the updated shared layout, display settings, and active tab.
    /// </summary>
    /// <param name="source">A sequence pairing the shared <see cref="GuiLayout"/> with the current <see cref="DataDisplaySettings"/>, tied to the render tick of DearImGui.</param>
    /// <returns>
    /// The updated <see cref="GuiLayout"/> and updated <see cref="DataDisplaySettings"/> paired with the
    /// currently active <see cref="ImageTab"/>.
    /// </returns>
    public unsafe IObservable<Tuple<GuiLayout, DataDisplaySettings, ImageTab>> Process(IObservable<Tuple<GuiLayout, DataDisplaySettings>> source)
    {
        return Observable.Create<Tuple<GuiLayout, DataDisplaySettings, ImageTab>>(observer =>
        {
            Task<string> overlayDialogTask = null;
            const nuint pathBufSize = 1024;
            bool wasPaused = false;
            CircularPlotPointSeries<Quaternion> quaternionSeries = null;
            CircularPlotPointSeries<TaitBryanAngles> eulerAnglesSeries = null;
            CircularPlotPointSeries<Tuple<bool, bool>> digitalInSeries = null;

            double eulerTimebase = TimebasePresetsSeconds[0];
            double quaternionTimebase = TimebasePresetsSeconds[0];
            bool eulerWasScrollable = false;
            bool quaternionWasScrollable = false;
            int eulerFrozenSamples = -1;
            int quaternionFrozenSamples = -1;
            SweepMarker eulerMarker = new(DataDisplaySettings.DefaultBufferSize);
            SweepMarker quaternionMarker = new(DataDisplaySettings.DefaultBufferSize);

            var sourceObserver = Observer.Create<Tuple<GuiLayout, DataDisplaySettings>>(
                value =>
                {
                    var layout = value.Item1;
                    var dataDisplaySettings = value.Item2;
                    var bufferSize = dataDisplaySettings.BufferSize;

                    string overlayReferencePath = dataDisplaySettings.Overlay.ReferencePath ?? string.Empty;
                    bool applyOverlay = dataDisplaySettings.Overlay.ApplyOverlay;
                    bool captureScreenshot = false;

                    var overlayReferenceColor = ConvertScalarColorToVector4(dataDisplaySettings.Overlay.ReferenceColor);
                    var overlayLiveColor = ConvertScalarColorToVector4(dataDisplaySettings.Overlay.LiveColor);

                    int satThreshold = dataDisplaySettings.Saturation.Threshold;
                    var satColor = ConvertScalarColorToVector4(dataDisplaySettings.Saturation.Color);

                    int backgroundFrames = dataDisplaySettings.Dff.BackgroundFrames;
                    double backgroundThreshold = dataDisplaySettings.Dff.BackgroundThreshold;
                    int sigma = dataDisplaySettings.Dff.Sigma;

                    var activeTab = ImageTab.None;
                    bool resetMaxProjection = false;

                    if (overlayDialogTask != null && overlayDialogTask.IsCompleted)
                    {
                        var chosen = overlayDialogTask.Result;
                        if (!string.IsNullOrEmpty(chosen))
                            overlayReferencePath = chosen;
                        overlayDialogTask = null;
                    }

                    if (!AcquisitionStatus && !ActiveImage.TexID.IsNull)
                    {
                        ActiveImage = default;
                    }

                    if (Paused && !wasPaused)
                    {
                        quaternionSeries = QuaternionSeries.Clone();
                        eulerAnglesSeries = EulerAnglesSeries.Clone();
                        digitalInSeries = DigitalInSeries.Clone();
                        wasPaused = true;
                    }
                    else if (!Paused)
                    {
                        quaternionSeries = QuaternionSeries;
                        eulerAnglesSeries = EulerAnglesSeries;
                        digitalInSeries = DigitalInSeries;
                        wasPaused = false;
                    }

                    void SetScreenshotCapture(bool flag) => captureScreenshot = flag;
                    void SetMaxProjectionReset(bool flag) => resetMaxProjection = flag;

                    bool textInputActive = ImGui.GetIO().WantTextInput;
                    bool screenshotButtonActive = AcquisitionStatus && !string.IsNullOrEmpty(DataPath);
                    bool fileMissing = string.IsNullOrEmpty(overlayReferencePath) || !File.Exists(overlayReferencePath);

                    if (screenshotButtonActive && !textInputActive && ImGui.IsKeyPressed(ImGuiKey.C))
                        SetScreenshotCapture(true);

                    if (!textInputActive && ImGui.IsKeyPressed(ImGuiKey.R))
                        SetMaxProjectionReset(true);

                    if (!textInputActive && ImGui.IsKeyPressed(ImGuiKey.E))
                        layout = layout with { ImageExpandedRequested = !layout.ImageExpanded };

                    if (!fileMissing && !textInputActive && ImGui.IsKeyPressed(ImGuiKey.O))
                        applyOverlay = !applyOverlay;

                    bool expanded = layout.ImageExpanded;
                    if (!expanded)
                        ImGui.SameLine();

                    float consoleReserve = layout.ReservedConsoleHeight(ImGui.GetStyle().ItemSpacing.Y);
                    if (ImGui.BeginChild("##Data", new Vector2(-1f, -consoleReserve)))
                    {
                        float totalHeight = ImGui.GetContentRegionAvail().Y;
                        float tabBarHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y;
                        float itemSpacingY = ImGui.GetStyle().ItemSpacing.Y;
                        float splitterReserve = ImageSplitterThickness + itemSpacingY * 2f;
                        float availableForPanes = Math.Max(0f, totalHeight - splitterReserve);

                        float imageChildHeight;
                        if (expanded)
                        {
                            imageChildHeight = totalHeight - tabBarHeight;
                        }
                        else
                        {
                            float imagePaneHeight = ClampImagePaneHeight(layout.ImagePaneHeight, availableForPanes);
                            layout = layout with { ImagePaneHeight = imagePaneHeight };
                            imageChildHeight = imagePaneHeight - tabBarHeight;
                        }

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
                                bool imageTabOpen = ImGui.BeginTabItem("Image##Image");
                                Tooltip.Describe("View the raw image and data frame information.");
                                if (imageTabOpen)
                                {
                                    activeTab = ImageTab.Raw;
                                    RenderImageArea("##image_area_raw", imageAreaSize, displaySize, ActiveImage);
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

                                        ImGui.Spacing();

                                        if (!screenshotButtonActive) ImGui.BeginDisabled();

                                        if (ImGui.Button("Capture Current Image (C)##overlay_screenshot", new Vector2(-1f, ButtonHeight)))
                                            SetScreenshotCapture(true);

                                        if (!screenshotButtonActive) ImGui.EndDisabled();

                                        if (Tooltip.Begin(allowWhenDisabled: true))
                                        {
                                            Tooltip.AddLine("Save a snapshot of the current image to the data path.");
                                            Tooltip.AddKeyboardShortcut("C");

                                            if (!AcquisitionStatus)
                                                Tooltip.Note("Unavailable while acquisition is stopped.");
                                            if (string.IsNullOrEmpty(DataPath))
                                                Tooltip.Note("Unavailable until a data path is set.");

                                            Tooltip.End();
                                        }

                                        if (RenderExpandCollapseButton(imageAreaHeight, layout.ImageExpanded))
                                            layout = layout with { ImageExpandedRequested = !layout.ImageExpanded };
                                    }
                                    EndControlColumn();

                                    ImGui.EndTabItem();
                                }

                                bool saturationTabOpen = ImGui.BeginTabItem("Saturation##Saturation");
                                Tooltip.Describe("View which pixels are saturated in the image.");
                                if (saturationTabOpen)
                                {
                                    activeTab = ImageTab.Saturation;
                                    RenderImageArea("##image_area_saturation", imageAreaSize, displaySize, ActiveImage);
                                    ImGui.SameLine();
                                    if (BeginControlColumn("##image_controls_saturation", controlColumnWidth, imageAreaHeight))
                                    {
                                        ImGui.TextUnformatted("Threshold:");
                                        ImGui.SetNextItemWidth(-1f);
                                        ImGui.SliderInt("##saturation_threshold", ref satThreshold, byte.MinValue, byte.MaxValue - 1, ImGuiSliderFlags.AlwaysClamp);
                                        Tooltip.Slider($"Pixels above this intensity value [{byte.MinValue} to {byte.MaxValue - 1}] are highlighted as saturated.");
                                        ImGui.Spacing();

                                        ImGui.TextUnformatted("Color:");
                                        if (ImGui.ColorEdit4("##saturation_color", ref satColor, ImGuiColorEditFlags.Uint8 | ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoOptions | ImGuiColorEditFlags.NoInputs))
                                        {
                                            satColor = ClampVector4Color(satColor);
                                        }
                                        Tooltip.Describe("Color used to highlight saturated pixels. Click to change it.");

                                        if (RenderExpandCollapseButton(imageAreaHeight, layout.ImageExpanded))
                                            layout = layout with { ImageExpandedRequested = !layout.ImageExpanded };
                                    }
                                    EndControlColumn();

                                    ImGui.EndTabItem();
                                }

                                bool dffTabOpen = ImGui.BeginTabItem("dF/F##dFF");
                                Tooltip.Describe(
                                    "View a naive, causal dF/F (delta-F over F)\n" +
                                    "calculated over the sequence of images.");
                                if (dffTabOpen)
                                {
                                    activeTab = ImageTab.Dff;
                                    RenderImageArea("##image_area_dff", imageAreaSize, displaySize, ActiveImage);
                                    ImGui.SameLine();
                                    if (BeginControlColumn("##image_controls_dff", controlColumnWidth, imageAreaHeight))
                                    {
                                        ImGui.TextUnformatted("Background frames:");
                                        ImGui.SetNextItemWidth(-1f);
                                        int backgroundFramesMin = 2, backgroundFramesMax = 1000;
                                        if (ImGui.InputInt("##background_frames", ref backgroundFrames))
                                            backgroundFrames = Math.Max(backgroundFramesMin, Math.Min(backgroundFramesMax, backgroundFrames));
                                        Tooltip.Describe(
                                            $"Number of previous frames averaged to determine the background\n" +
                                            $"fluorescence used in the dF/F calculation [{backgroundFramesMin} to {backgroundFramesMax} frames].");
                                        ImGui.Spacing();

                                        ImGui.TextUnformatted("Background threshold:");
                                        ImGui.SetNextItemWidth(-1f);
                                        double bgThreshMin = 0, bgThreshMax = 255;
                                        ImGui.SliderScalar("##background_threshold", ImGuiDataType.Double, &backgroundThreshold, &bgThreshMin, &bgThreshMax, "%.1f", ImGuiSliderFlags.AlwaysClamp);
                                        Tooltip.Slider(
                                            $"Minimum background intensity [{bgThreshMin} to {bgThreshMax}] required to calculate dF/F for a pixel.");
                                        ImGui.Spacing();

                                        ImGui.TextUnformatted("Sigma (px):");
                                        ImGui.SetNextItemWidth(-1f);
                                        if (ImGui.InputInt("##sigma", ref sigma))
                                            sigma = Math.Max(0, sigma);
                                        Tooltip.Describe(
                                            "Standard deviation, in pixels, of the Gaussian blur applied before computing dF/F.\n" +
                                            "Approximates the spatial extent of a typical region of interest (i.e., a cell body),\n" +
                                            "to help smooth out pixel-level noise. Set to 0 to disable blurring.");

                                        if (RenderExpandCollapseButton(imageAreaHeight, layout.ImageExpanded))
                                            layout = layout with { ImageExpandedRequested = !layout.ImageExpanded };
                                    }
                                    EndControlColumn();

                                    ImGui.EndTabItem();
                                }

                                bool maxProjectionTabOpen = ImGui.BeginTabItem("Max Projection##MaxProjection");
                                Tooltip.Describe(
                                    "View the accumulated maximum projection intensity from the images.\n" +
                                    "Maximum projection intensity is continuously accumulated until manually reset.");
                                if (maxProjectionTabOpen)
                                {
                                    activeTab = ImageTab.MaxProjection;
                                    RenderImageArea("##image_area_maxprojection", imageAreaSize, displaySize, ActiveImage);
                                    ImGui.SameLine();
                                    if (BeginControlColumn("##image_controls_maxprojection", controlColumnWidth, imageAreaHeight))
                                    {
                                        ImGui.TextUnformatted("Max pixel-value projection");
                                        ImGui.Spacing();

                                        if (ImGui.Button("Reset (R)##maxprojection_reset", new Vector2(-1f, 0f)))
                                            SetMaxProjectionReset(true);

                                        if (Tooltip.Begin(allowWhenDisabled: true))
                                        {
                                            Tooltip.AddLine(
                                                "Clear the accumulated projection and start building\n" +
                                                "it again from the current frame.");
                                            Tooltip.AddKeyboardShortcut("R");
                                            Tooltip.End();
                                        }

                                        if (RenderExpandCollapseButton(imageAreaHeight, layout.ImageExpanded))
                                            layout = layout with { ImageExpandedRequested = !layout.ImageExpanded };
                                    }
                                    EndControlColumn();

                                    ImGui.EndTabItem();
                                }

                                bool referenceImageTabOpen = ImGui.BeginTabItem("Reference Image##reference_image");
                                Tooltip.Describe(
                                    "View the live image overlaid on a static reference image\n" +
                                    "(e.g., a previous captured image) to help align the current field of view.");
                                if (referenceImageTabOpen)
                                {
                                    activeTab = ImageTab.Overlay;
                                    bool showImage = !string.IsNullOrEmpty(overlayReferencePath) || applyOverlay;
                                    var image = showImage ? ActiveImage : default;

                                    RenderImageArea("##image_area_overlay", imageAreaSize, displaySize, image);

                                    ImGui.SameLine();
                                    if (BeginControlColumn("##image_controls_overlay", controlColumnWidth, imageAreaHeight))
                                    {
                                        ImGui.TextUnformatted("Reference Image");

                                        const string selectLabel = "...";
                                        const string browseLabel = "Browse";
                                        var (selectWidth, browseWidth, inputWidth) = FilePanel.CalculateFileNameInputWidth(selectLabel, browseLabel);

                                        ImGui.SetNextItemWidth(inputWidth);
                                        ImGui.InputText("##overlay_path", ref overlayReferencePath, pathBufSize, ImGuiInputTextFlags.ReadOnly | ImGuiInputTextFlags.ElideLeft);

                                        ImGui.SameLine();
                                        if (ImGui.Button($"{selectLabel}##choose_screenshot", new Vector2(selectWidth, 0)))
                                        {
                                            if (overlayDialogTask == null || overlayDialogTask.IsCompleted)
                                            {
                                                overlayDialogTask = FileDialogHelpers.RunDialogTask(() => new OpenFileDialog
                                                {
                                                    Filter = "Images|*.png;*.tif;*.tiff;*.jpg;*.bmp|All Files|*.*",
                                                    CheckFileExists = true,
                                                    Multiselect = false,
                                                    InitialDirectory = FileDialogHelpers.GetDirectory(DataPath),
                                                    Title = "Choose a captured image to load.",
                                                },
                                                (dlg) => (dlg as OpenFileDialog).FileName);
                                            }
                                        }
                                        Tooltip.Describe("Choose a reference image (e.g., a previous captured image) to overlay on the live view.");

                                        ImGui.SameLine();
                                        if (ImGui.Button($"{browseLabel}##browse_screenshots", new Vector2(browseWidth, 0)))
                                        {
                                            var dir = FileDialogHelpers.GetDirectory(DataPath);
                                            if (Directory.Exists(dir))
                                                System.Diagnostics.Process.Start("explorer.exe", dir);
                                        }
                                        Tooltip.Describe("Open the data folder in File Explorer to browse for previous captured images.");

                                        if (fileMissing) ImGui.BeginDisabled();

                                        ImGui.Checkbox("Apply Live Overlay (O)", ref applyOverlay);
                                        if (Tooltip.Begin(allowWhenDisabled: true))
                                        {
                                            Tooltip.AddLine("Overlay the live image on the reference image to align the current field of view with a previous one.");
                                            Tooltip.AddKeyboardShortcut("O");
                                            if (fileMissing)
                                                Tooltip.Note("Unavailable until a valid reference image is chosen.");
                                            Tooltip.End();
                                        }

                                        if (fileMissing) ImGui.EndDisabled();

                                        ImGui.Spacing();

                                        ImGui.TextUnformatted("Reference Color:");
                                        if (ImGui.ColorEdit4("##overlay_reference_color", ref overlayReferenceColor, ImGuiColorEditFlags.Uint8 | ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoOptions | ImGuiColorEditFlags.NoInputs))
                                        {
                                            overlayReferenceColor = ClampVector4Color(overlayReferenceColor);
                                        }
                                        Tooltip.Describe("Color used to tint the reference image in the overlay. Click to change it.");

                                        ImGui.TextUnformatted("Live Color:");
                                        if (ImGui.ColorEdit4("##overlay_live_color", ref overlayLiveColor, ImGuiColorEditFlags.Uint8 | ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoOptions | ImGuiColorEditFlags.NoInputs))
                                        {
                                            overlayLiveColor = ClampVector4Color(overlayLiveColor);
                                        }
                                        Tooltip.Describe("Color used to tint the live image in the overlay. Click to change it.");

                                        if (RenderExpandCollapseButton(imageAreaHeight, layout.ImageExpanded))
                                            layout = layout with { ImageExpandedRequested = !layout.ImageExpanded };
                                    }

                                    EndControlColumn();

                                    ImGui.EndTabItem();
                                }

                                ImGui.EndTabBar();
                            }
                        }

                        ImGui.EndChild();

                        if (!expanded)
                        {
                            ImGui.InvisibleButton("##data_splitter", new Vector2(-1f, ImageSplitterThickness));

                            bool hovered = ImGui.IsItemHovered();
                            bool active = ImGui.IsItemActive();

                            if (active)
                                layout = layout with { ImagePaneHeight = ClampImagePaneHeight(layout.ImagePaneHeight + ImGui.GetIO().MouseDelta.Y, availableForPanes) };
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
                                float* subplotRowRatios = stackalloc float[] { 0.7f, 0.3f };
                                double digitalAxisLimitsMin = -0.05, digitalAxisLimitsMax = 2.05;

                                if (ImGui.BeginTabBar("##SignalTabBar"))
                                {
                                    ImPlotAxisFlags axisFlags = ImPlotAxisFlags.AutoFit | ImPlotAxisFlags.NoMenus | ImPlotAxisFlags.NoTickMarks | ImPlotAxisFlags.NoGridLines | ImPlotAxisFlags.NoTickLabels;

                                    Vector2 CalculateChildHeight() => new(-1f, Math.Max(MinSignalPaneHeight * 0.65f, ImGui.GetContentRegionAvail().Y));

                                    bool scrollable = Paused || !AcquisitionStatus;
                                    var frameRate = SelectedFrameRate;
                                    var fps = ConvertFrameRateV4ToFps(frameRate) ?? 0;
                                    ImPlotAxisFlags xAxisFlags = scrollable ? (axisFlags & ~ImPlotAxisFlags.AutoFit) : (axisFlags | ImPlotAxisFlags.Lock);
                                    ImPlotAxisFlags yAxisFlags = axisFlags | ImPlotAxisFlags.Lock | ImPlotAxisFlags.NoHighlight;
                                    ImPlotFlags signalPlotFlags = scrollable
                                        ? (plotFlags & ~ImPlotFlags.NoInputs) | ImPlotFlags.NoMouseText
                                        : plotFlags | ImPlotFlags.NoMouseText;

                                    bool eulerTabOpen = ImGui.BeginTabItem("Euler Angles");
                                    Tooltip.Describe("View the Euler angles, which are displayed as Yaw, Pitch, and Roll,\n" +
                                        "as defined by the Tait-Bryan formalism.");
                                    if (eulerTabOpen)
                                    {
                                        var controlsHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ScrollbarSize;
                                        var eulerTimebaseBeforeControl = eulerTimebase;
                                        if (ImGui.BeginChild("##euler_controls", new Vector2(0f, controlsHeight), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
                                        {
                                            TimebaseControl(frameRate, bufferSize, ref eulerTimebase);
                                            ImGui.SameLine();
                                            eulerAngleLegend.DrawSameLine();
                                            digitalInLegend.DrawSameLine();
                                        }
                                        if (eulerTimebase != eulerTimebaseBeforeControl)
                                        {
                                            eulerFrozenSamples = -1;
                                            eulerWasScrollable = false;
                                        }

                                        ImGui.EndChild();

                                        if (ImGui.BeginChild("##euler_child", CalculateChildHeight()))
                                        {
                                            if (ImPlot.BeginSubplots("##euler_subplots", 2, 1, fillAvailable, signalSubplotFlags, subplotRowRatios, null))
                                            {
                                                var numSamples = GetNumberOfSamples(frameRate, eulerTimebase);
                                                if (!scrollable)
                                                    eulerFrozenSamples = -1;
                                                var windowSamples = scrollable ? (eulerFrozenSamples < 0 ? (eulerFrozenSamples = numSamples) : eulerFrozenSamples) : numSamples;
                                                var eulerWindow = PlotWindow.Create(eulerAnglesSeries, windowSamples, bufferSize, scrollable, ref eulerMarker);
                                                var eulerXAxisLimits = GetXAxisLimits(scrollable, ref eulerWasScrollable, eulerWindow);

                                                if (ImPlot.BeginPlot("##euler_angles_series", fillAvailable, signalPlotFlags))
                                                {
                                                    const double eulerYAxisMin = -185.0, eulerYAxisMax = 365.0;

                                                    ImPlot.SetupAxes("", "", xAxisFlags, yAxisFlags);
                                                    if (scrollable)
                                                        ImPlot.SetupAxisLimitsConstraints(ImAxis.X1, -eulerWindow.ExtendedCount, eulerWindow.XAxisMax);
                                                    if (eulerXAxisLimits is (double eulerXAxisMin, double eulerXAxisMax))
                                                        ImPlot.SetupAxisLimits(ImAxis.X1, eulerXAxisMin, eulerXAxisMax, ImPlotCond.Always);
                                                    ImPlot.SetupAxisLimits(ImAxis.Y1, TimeLabelLimit(eulerYAxisMin, eulerYAxisMax), eulerYAxisMax, ImPlotCond.Always);

                                                    float eulerDataPixelSpan = ImPlot.GetPlotSize().Y * (float)(1.0 - TimeLabelFraction);
                                                    double eulerGridStep = ChooseGridStep(eulerYAxisMax - eulerYAxisMin, eulerDataPixelSpan, eulerGridStepCandidates, MinGridPixelSpacing * UiScale.Current);
                                                    DrawPlotGrid(eulerYAxisMin, eulerYAxisMax, eulerGridStep, FormatDegreeLabel);
                                                    DrawTimeGrid(fps, eulerYAxisMin);

                                                    if (eulerAnglesSeries != null)
                                                    {
                                                        PlotCircularPlotPointSeries(eulerAnglesSeries, eulerAngleLegend, eulerWindow);
                                                        SyncTimebaseWithAxisZoom(frameRate, scrollable, bufferSize, ref eulerTimebase);
                                                    }

                                                    ImPlot.EndPlot();
                                                }

                                                if (ImPlot.BeginPlot("##euler_digital_series", fillAvailable, signalPlotFlags))
                                                {
                                                    ImPlot.SetupAxes("", "", xAxisFlags, yAxisFlags);
                                                    if (scrollable)
                                                        ImPlot.SetupAxisLimitsConstraints(ImAxis.X1, -eulerWindow.ExtendedCount, eulerWindow.XAxisMax);
                                                    if (eulerXAxisLimits is (double eulerDigitalXAxisMin, double eulerDigitalXAxisMax))
                                                        ImPlot.SetupAxisLimits(ImAxis.X1, eulerDigitalXAxisMin, eulerDigitalXAxisMax, ImPlotCond.Always);
                                                    ImPlot.SetupAxisLimits(ImAxis.Y1, digitalAxisLimitsMin, digitalAxisLimitsMax, ImPlotCond.Always);

                                                    PlotDigitalInSeries(digitalInSeries, digitalInLegend, digitalInLabels, eulerWindow);

                                                    ImPlot.EndPlot();
                                                }
                                            }

                                            ImPlot.EndSubplots();
                                        }
                                        ImGui.EndChild();

                                        ImGui.EndTabItem();
                                    }

                                    bool quaternionTabOpen = ImGui.BeginTabItem("Quaternion");
                                    Tooltip.Describe("View the raw quaternion data reported by the Miniscope, which can\n" +
                                        "automatically rotate an attached commutator if one is connected.");
                                    if (quaternionTabOpen)
                                    {
                                        var controlsHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ScrollbarSize;
                                        var quaternionTimebaseBeforeControl = quaternionTimebase;
                                        if (ImGui.BeginChild("##quat_controls", new Vector2(0f, controlsHeight), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
                                        {
                                            TimebaseControl(frameRate, bufferSize, ref quaternionTimebase);
                                            ImGui.SameLine();
                                            quaternionLegend.DrawSameLine();
                                            digitalInLegend.DrawSameLine();
                                        }
                                        if (quaternionTimebase != quaternionTimebaseBeforeControl)
                                        {
                                            quaternionFrozenSamples = -1;
                                            quaternionWasScrollable = false;
                                        }

                                        ImGui.EndChild();

                                        if (ImGui.BeginChild("##quaternion_child", CalculateChildHeight()))
                                        {
                                            if (ImPlot.BeginSubplots("##quaternion_subplots", 2, 1, fillAvailable, signalSubplotFlags, subplotRowRatios, null))
                                            {
                                                var numSamples = GetNumberOfSamples(frameRate, quaternionTimebase);
                                                if (!scrollable)
                                                    quaternionFrozenSamples = -1;
                                                var windowSamples = scrollable ? (quaternionFrozenSamples < 0 ? (quaternionFrozenSamples = numSamples) : quaternionFrozenSamples) : numSamples;
                                                var quaternionWindow = PlotWindow.Create(quaternionSeries, windowSamples, bufferSize, scrollable, ref quaternionMarker);
                                                var quaternionXAxisLimits = GetXAxisLimits(scrollable, ref quaternionWasScrollable, quaternionWindow);

                                                if (ImPlot.BeginPlot("##quaternion_series", fillAvailable, signalPlotFlags))
                                                {
                                                    const double quaternionAxisMin = -1.05, quaternionAxisMax = 1.05;
                                                    const double quaternionGridStep = 0.5;

                                                    ImPlot.SetupAxes("", "", xAxisFlags, yAxisFlags);
                                                    if (scrollable)
                                                        ImPlot.SetupAxisLimitsConstraints(ImAxis.X1, -quaternionWindow.ExtendedCount, quaternionWindow.XAxisMax);
                                                    if (quaternionXAxisLimits is (double quaternionXAxisMin, double quaternionXAxisMax))
                                                        ImPlot.SetupAxisLimits(ImAxis.X1, quaternionXAxisMin, quaternionXAxisMax, ImPlotCond.Always);
                                                    ImPlot.SetupAxisLimits(ImAxis.Y1, TimeLabelLimit(quaternionAxisMin, quaternionAxisMax), quaternionAxisMax, ImPlotCond.Always);

                                                    DrawPlotGrid(quaternionAxisMin, quaternionAxisMax, quaternionGridStep, null);
                                                    DrawTimeGrid(fps, quaternionAxisMin);

                                                    if (quaternionSeries != null)
                                                    {
                                                        PlotCircularPlotPointSeries(quaternionSeries, quaternionLegend, quaternionWindow);
                                                        SyncTimebaseWithAxisZoom(frameRate, scrollable, bufferSize, ref quaternionTimebase);
                                                    }

                                                    ImPlot.EndPlot();
                                                }

                                                if (ImPlot.BeginPlot("##quaternion_digital_series", fillAvailable, signalPlotFlags))
                                                {
                                                    ImPlot.SetupAxes("", "", xAxisFlags, yAxisFlags);
                                                    if (scrollable)
                                                        ImPlot.SetupAxisLimitsConstraints(ImAxis.X1, -quaternionWindow.ExtendedCount, quaternionWindow.XAxisMax);
                                                    if (quaternionXAxisLimits is (double quaternionDigitalXAxisMin, double quaternionDigitalXAxisMax))
                                                        ImPlot.SetupAxisLimits(ImAxis.X1, quaternionDigitalXAxisMin, quaternionDigitalXAxisMax, ImPlotCond.Always);
                                                    ImPlot.SetupAxisLimits(ImAxis.Y1, digitalAxisLimitsMin, digitalAxisLimitsMax, ImPlotCond.Always);

                                                    PlotDigitalInSeries(digitalInSeries, digitalInLegend, digitalInLabels, quaternionWindow);

                                                    ImPlot.EndPlot();
                                                }
                                            }
                                            ImPlot.EndSubplots();
                                        }
                                        ImGui.EndChild();

                                        ImGui.EndTabItem();
                                    }

                                    bool histogramTabOpen = ImGui.BeginTabItem("Histogram");
                                    Tooltip.Describe(
                                        "View a normalized distribution of pixel intensity across the image.\n" +
                                        "The histogram indicates the relative distribution of pixels\n" +
                                        "but does not show the number of pixels at any given intensity.");
                                    if (histogramTabOpen)
                                    {
                                        if (ImageHistogram != null)
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

                                            if (ImGui.BeginChild("##histogram_child", CalculateChildHeight()))
                                            {
                                                if (ImPlot.BeginPlot("##histogram", fillAvailable, plotFlags))
                                                {
                                                    double minValue = 0, maxValue = byte.MaxValue, axisOffset = 5;

                                                    ImPlot.SetupAxes("", "", flagsX, flagsY);
                                                    ImPlot.SetupAxisLimits(ImAxis.X1, minValue - axisOffset, maxValue + axisOffset, ImPlotCond.Always);

                                                    fixed (float* binPtr = bins)
                                                    {
                                                        ImPlot.PlotBars("##pixel_intensity", binPtr, hist.Bins.GetDimSize(0), 2.0f);
                                                    }

                                                    ImPlot.EndPlot();
                                                }
                                            }
                                            ImGui.EndChild();
                                        }

                                        ImGui.EndTabItem();
                                    }

                                    ImGui.EndTabBar();
                                }
                            }

                            ImGui.EndChild();
                        }
                    }

                    ImGui.EndChild();

                    var updatedDisplaySettings = new DataDisplaySettings
                    {
                        Saturation = new SaturationSettings { Threshold = satThreshold, Color = ConvertVector4ColorToScalar(satColor) },
                        Dff = new DffSettings { BackgroundFrames = backgroundFrames, BackgroundThreshold = backgroundThreshold, Sigma = sigma },
                        MaxProjection = new MaxProjectionSettings { Reset = resetMaxProjection },
                        Overlay = new OverlaySettings
                        {
                            Capture = captureScreenshot,
                            ApplyOverlay = applyOverlay,
                            ReferencePath = overlayReferencePath,
                            ReferenceColor = ConvertVector4ColorToScalar(overlayReferenceColor),
                            LiveColor = ConvertVector4ColorToScalar(overlayLiveColor),
                        },
                    };

                    observer.OnNext(Tuple.Create(layout, updatedDisplaySettings, activeTab));
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

    static float ButtonHeight => ImGui.GetFrameHeight() * 2f;

    static bool RenderExpandCollapseButton(float columnHeight, bool imageExpanded)
    {
        var buttonHeight = ButtonHeight;
        float targetY = columnHeight - buttonHeight;
        if (targetY > ImGui.GetCursorPosY())
            ImGui.SetCursorPosY(targetY);

        bool clicked = ImGui.Button(imageExpanded ? "Collapse (E)##image_expand_toggle" : "Expand (E)##image_expand_toggle", new Vector2(-1f, buttonHeight));

        if (Tooltip.Begin())
        {
            if (imageExpanded)
            {
                Tooltip.AddLine("Restore the side panels and signal plots.");
                Tooltip.AddKeyboardShortcut("E");
            }
            else
            {
                Tooltip.AddLine("Expand the image to fill the window, hiding the side panels and signal plots.");
                Tooltip.AddKeyboardShortcut("E");
            }
            Tooltip.End();
        }

        return clicked;
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

    static int Mod(int value, int modulus) => (value % modulus + modulus) % modulus;

    struct SweepMarker
    {
        double fraction;
        int previousEnd;
        readonly int bufferCapacity;

        public SweepMarker(int bufferCapacity)
        {
            fraction = 0;
            previousEnd = 0;
            this.bufferCapacity = bufferCapacity;
        }

        public int Advance(int end, int samplesToPlot)
        {
            if (samplesToPlot <= 0)
                return 0;

            var newSamples = Mod(end - previousEnd, bufferCapacity);
            previousEnd = end;

            var previous = Math.Min((int)Math.Round(fraction * samplesToPlot), samplesToPlot - 1);
            var position = Mod(previous + newSamples, samplesToPlot);
            fraction = (double)position / samplesToPlot;
            return position;
        }
    }

    readonly struct PlotWindow
    {
        readonly int samplesToPlot;
        readonly int bufferCapacity;
        readonly int windowStart;
        readonly int zeroRank;

        /// <summary>Gets the position of the newest sample.</summary>
        public int Marker { get; }

        /// <summary>
        /// Gets the run holding the oldest data on screen, drawn up to the right edge of the window.
        /// </summary>
        public (int Start, int Rank, int Count) Head { get; }

        /// <summary>Gets the run drawn from the left edge of the window, after the sweep wraps around.</summary>
        public (int Start, int Rank, int Count) Tail { get; }

        /// <summary>
        /// Gets the number of buffered samples older than the drawn ones, which are drawn behind the
        /// left edge of the window so they can be scrolled into view. Zero unless the display is scrollable.
        /// </summary>
        public int ExtendedCount { get; }

        /// <summary>Gets the upper limit of the x-axis.</summary>
        public int XAxisMax => Math.Max(0, samplesToPlot - 1);

        PlotWindow(int count, int end, int marker, int samplesToPlot, int bufferCapacity, bool scrollable)
        {
            this.samplesToPlot = samplesToPlot;
            this.bufferCapacity = bufferCapacity;
            Marker = marker;

            var drawCount = Math.Min(count, samplesToPlot);
            var drawStart = samplesToPlot == 0 ? 0 : Mod(marker - drawCount + 1, samplesToPlot);
            var headCount = Math.Min(drawCount, samplesToPlot - drawStart);
            var tailCount = drawCount - headCount;

            var overlap = count > drawCount && drawStart > 0 ? 1 : 0;
            Head = (drawStart - overlap, -overlap, headCount + overlap);
            Tail = (0, headCount, tailCount);

            windowStart = Mod(end - drawCount, bufferCapacity);
            zeroRank = tailCount > 0 ? headCount : 0;

            ExtendedCount = scrollable && count >= samplesToPlot ? count - 1 - marker : 0;
        }

        public static PlotWindow Create<T>(CircularPlotPointSeries<T> series, int samplesToPlot, int bufferCapacity, bool scrollable, ref SweepMarker marker)
        {
            var end = series?.End ?? 0;
            return new(series?.Count ?? 0, end, marker.Advance(end, samplesToPlot), samplesToPlot, bufferCapacity, scrollable);
        }

        public int PhysicalSlot(int rank) => Mod(windowStart + rank, bufferCapacity);

        public int ExtendedPhysicalSlot(int k) => PhysicalSlot(zeroRank - k);
    }

    static unsafe void PlotCircularPlotPointSeries<T>(CircularPlotPointSeries<T> buffer, PlotLegend legend, PlotWindow window)
    {
        if (buffer == null)
            return;

        int extendedCount = window.ExtendedCount;

        for (int i = 0; i < buffer.Series.Length; i++)
        {
            if (!legend.IsVisible(i))
                continue;

            var line = buffer.Series[i];
            var color = legend.ColorOf(i);
            var run = window.Head;

            nint remappedGetter(nint data, int index, nint pointPtr)
            {
                int physicalSlot = window.PhysicalSlot(run.Rank + index);
                nint result = line.Getter(data, physicalSlot, pointPtr);
                var point = (ImPlotPoint*)pointPtr;
                point->X = run.Start + index;
                return result;
            }

            ImPlotPointGetter remappedGetterDelegate = remappedGetter;

            ImPlot.SetNextLineStyle(color);
            ImPlot.PlotLineG(line.Name, remappedGetterDelegate, null, run.Count);

            if (window.Tail.Count > 0)
            {
                run = window.Tail;
                ImPlot.SetNextLineStyle(color);
                ImPlot.PlotLineG(line.Name + "##wrap", remappedGetterDelegate, null, run.Count);
            }

            GC.KeepAlive(remappedGetterDelegate);

            if (extendedCount > 0)
            {
                nint extendedGetter(nint data, int k, nint pointPtr)
                {
                    int physicalSlot = window.ExtendedPhysicalSlot(k);
                    nint result = line.Getter(data, physicalSlot, pointPtr);
                    var point = (ImPlotPoint*)pointPtr;
                    point->X = -k;
                    return result;
                }

                ImPlot.SetNextLineStyle(color);
                ImPlotPointGetter extendedGetterDelegate = extendedGetter;
                ImPlot.PlotLineG(line.Name + "##ext", extendedGetterDelegate, null, extendedCount + 1);
                GC.KeepAlive(extendedGetterDelegate);
            }
        }

        PlotVerticalLine((float)window.Marker, Palette.Yellow);
    }

    static unsafe void PlotVerticalLine(float x, Vector4 color)
    {
        ImPlot.PushStyleColor(ImPlotCol.Line, color);
        ImPlot.PlotInfLines("##end_index", &x, 1);
        ImPlot.PopStyleColor();
    }

    static unsafe void PlotDigitalInSeries<T>(CircularPlotPointSeries<T> buffer, PlotLegend legend, string[] labels, PlotWindow window)
    {
        if (buffer == null)
            return;

        // NB: Unlike the other plots, this one shares the x-axis of the plot above it and so is drawn with a
        // window built from *that* buffer, not from this one. The plot buffers are separate subscriptions torn
        // down and rebuilt independently on each acquisition transition, so this one can briefly hold fewer
        // samples than the window wants to draw. Never ask the getters for more points than this buffer has.
        int available = buffer.Count;

        int extendedCount = Math.Min(window.ExtendedCount, available);
        int extendedStepCount = extendedCount > 0 ? 2 * extendedCount : 0;

        for (int i = 0; i < buffer.Series.Length; i++)
        {
            if (!legend.IsVisible(i))
                continue;

            var line = buffer.Series[i];
            var color = legend.ColorOf(i);
            var run = window.Head;

            double baseline = i;

            nint valueGetter(nint data, int idx, nint pointPtr)
            {
                int offset = idx / 2;
                int physicalSlot = window.PhysicalSlot(run.Rank + offset);
                nint result = line.Getter(data, physicalSlot, pointPtr);
                var point = (ImPlotPoint*)pointPtr;
                point->X = run.Start + offset + (idx & 1);
                point->Y = baseline + point->Y * DigitalAmplitude;
                return result;
            }

            nint baselineGetter(nint data, int idx, nint pointPtr)
            {
                int offset = idx / 2;
                int physicalSlot = window.PhysicalSlot(run.Rank + offset);
                nint result = line.Getter(data, physicalSlot, pointPtr);
                var point = (ImPlotPoint*)pointPtr;
                point->X = run.Start + offset + (idx & 1);
                point->Y = baseline;
                return result;
            }

            ImPlotPointGetter valueGetterDelegate = valueGetter;
            ImPlotPointGetter baselineGetterDelegate = baselineGetter;

            void PlotRun(string suffix, (int Start, int Rank, int Count) segment)
            {
                int count = Math.Min(segment.Count, available);
                if (count <= 0)
                    return;

                run = segment;
                int stepCount = count > 1 ? 2 * count - 1 : 1;

                ImPlot.SetNextFillStyle(color);
                ImPlot.PlotShadedG(labels[i] + "##fill" + suffix, valueGetterDelegate, null, baselineGetterDelegate, null, stepCount);

                ImPlot.SetNextLineStyle(color);
                ImPlot.PlotLineG(labels[i] + "##line" + suffix, valueGetterDelegate, null, stepCount);
            }

            PlotRun(string.Empty, window.Head);
            PlotRun("##wrap", window.Tail);

            GC.KeepAlive(valueGetterDelegate);
            GC.KeepAlive(baselineGetterDelegate);

            if (extendedStepCount > 0)
            {
                nint extValueGetter(nint data, int idx, nint pointPtr)
                {
                    int j = idx / 2;
                    int k = extendedCount - j;
                    int physicalSlot = window.ExtendedPhysicalSlot(k);
                    nint result = line.Getter(data, physicalSlot, pointPtr);
                    var point = (ImPlotPoint*)pointPtr;
                    double heldY = baseline + point->Y * DigitalAmplitude;
                    point->X = (idx & 1) == 0 ? -k : -(k - 1);
                    point->Y = heldY;
                    return result;
                }

                nint extBaselineGetter(nint data, int idx, nint pointPtr)
                {
                    int j = idx / 2;
                    int k = extendedCount - j;
                    int physicalSlot = window.ExtendedPhysicalSlot(k);
                    nint result = line.Getter(data, physicalSlot, pointPtr);
                    var point = (ImPlotPoint*)pointPtr;
                    point->X = (idx & 1) == 0 ? -k : -(k - 1);
                    point->Y = baseline;
                    return result;
                }

                ImPlotPointGetter extValueGetterDelegate = extValueGetter;
                ImPlotPointGetter extBaselineGetterDelegate = extBaselineGetter;

                ImPlot.SetNextFillStyle(color);
                ImPlot.PlotShadedG(labels[i] + "##fill_ext", extValueGetterDelegate, null, extBaselineGetterDelegate, null, extendedStepCount);

                ImPlot.SetNextLineStyle(color);
                ImPlot.PlotLineG(labels[i] + "##line_ext", extValueGetterDelegate, null, extendedStepCount);

                GC.KeepAlive(extValueGetterDelegate);
                GC.KeepAlive(extBaselineGetterDelegate);
            }
        }

        PlotVerticalLine((float)window.Marker, Palette.Yellow);
    }
}
