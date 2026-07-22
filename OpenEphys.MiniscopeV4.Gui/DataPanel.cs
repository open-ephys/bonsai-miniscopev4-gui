using Bonsai;
using Bonsai.ImGui.Visualizers;
using Bonsai.Vision;
using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;
using OpenCV.Net;
using OpenEphys.Miniscope;
using System;
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
    /// Gets or sets the rolling series of Euler angle values plotted in the time series tab.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public RollingPlotPointSeries<TaitBryanAngles> EulerAnglesSeries { get; set; }

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

    /// <summary>
    /// Gets or sets the data path set by <see cref="FilePanel"/>.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public string DataPath { get; set; }

    static float ControlColumnWidth => 220f * UiScale.Current;

    const float BaseMinImagePaneHeight = 100f;
    const float BaseMinSignalPaneHeight = 80f;

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

    static readonly Vector2 fillAvailable = new(-1, -1);
    static readonly ImPlotFlags plotFlags = ImPlotFlags.NoMenus | ImPlotFlags.NoInputs | ImPlotFlags.NoTitle | ImPlotFlags.NoLegend;
    static readonly string[] digitalInLabels = new string[] { MiniscopeDaqDigitalIn.DigitalIn0.ToString(), MiniscopeDaqDigitalIn.DigitalIn1.ToString() };
    static readonly string[] histogramAxisTickLabels = new string[] { "0%", "20%", "40%", "60%", "80%", "100%" };

    static readonly PlotLegend quaternionLegend = new(
        "quaternion",
        new PlotLegend.Entry("X", Palette.RedHovered),
        new PlotLegend.Entry("Y", Palette.GreenHovered),
        new PlotLegend.Entry("Z", Palette.BlueHovered),
        new PlotLegend.Entry("W", Palette.PurpleHovered));
    static readonly PlotLegend digitalInLegend = new(
        "digitalin",
        new PlotLegend.Entry(digitalInLabels[0], Palette.YellowHovered),
        new PlotLegend.Entry(digitalInLabels[1], new Vector4(0.256f, 0.700f, 0.800f, 1f)));
    static readonly PlotLegend eulerAngleLegend = new(
        "euler_angles",
        new PlotLegend.Entry("Yaw", Palette.RedHovered),
        new PlotLegend.Entry("Pitch", Palette.GreenHovered),
        new PlotLegend.Entry("Roll", Palette.BlueHovered));

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

            var sourceObserver = Observer.Create<Tuple<GuiLayout, DataDisplaySettings>>(
                value =>
                {
                    var layout = value.Item1;
                    var dataDisplaySettings = value.Item2;
                    var bufferSize = dataDisplaySettings.BufferSize;

                    string overlayReferencePath = dataDisplaySettings.Overlay.ReferencePath ?? string.Empty;
                    bool applyOverlay = dataDisplaySettings.Overlay.ApplyOverlay;
                    bool captureScreenshot = false;

                    int satThreshold = dataDisplaySettings.Saturation.Threshold;
                    var satColor = new Vector4(
                        (float)dataDisplaySettings.Saturation.Color.Val2 / 255,
                        (float)dataDisplaySettings.Saturation.Color.Val1 / 255,
                        (float)dataDisplaySettings.Saturation.Color.Val0 / 255,
                        (float)dataDisplaySettings.Saturation.Color.Val3 / 255);

                    int backgroundFrames = dataDisplaySettings.Dff.BackgroundFrames;
                    double backgroundThreshold = dataDisplaySettings.Dff.BackgroundThreshold;
                    int sigma = dataDisplaySettings.Dff.Sigma;

                    var activeTab = ImageTab.None;
                    bool resetMaxProjection = false;

                    if (!AcquisitionStatus && !ActiveImage.TexID.IsNull)
                    {
                        ActiveImage = default;
                    }

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
                                if (ImGui.BeginTabItem("Image##Image"))
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

                                        if (!AcquisitionStatus || string.IsNullOrEmpty(DataPath)) ImGui.BeginDisabled();

                                        if (ImGui.Button("Take Screenshot##overlay_screenshot", new Vector2(-1f, ButtonHeight)))
                                            captureScreenshot = true;

                                        if (!AcquisitionStatus || string.IsNullOrEmpty(DataPath)) ImGui.EndDisabled();

                                        if (ImGui.BeginItemTooltip())
                                        {
                                            ImGui.Text("Take a screenshot of the current image.");

                                            if (!AcquisitionStatus)
                                                ImGui.Text("Cannot take a screenshot while acquisition is stopped.");
                                            else if (string.IsNullOrEmpty(DataPath))
                                                ImGui.Text("Cannot take a screenshot because the data path is not set.");

                                            ImGui.EndTooltip();
                                        }

                                        layout = layout with { ImageExpanded = RenderExpandCollapseButton(imageAreaHeight, layout.ImageExpanded) };
                                    }
                                    EndControlColumn();

                                    ImGui.EndTabItem();
                                }

                                if (ImGui.BeginTabItem("Saturation##Saturation"))
                                {
                                    activeTab = ImageTab.Saturation;
                                    RenderImageArea("##image_area_saturation", imageAreaSize, displaySize, ActiveImage);
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

                                        layout = layout with { ImageExpanded = RenderExpandCollapseButton(imageAreaHeight, layout.ImageExpanded) };
                                    }
                                    EndControlColumn();

                                    ImGui.EndTabItem();
                                }

                                if (ImGui.BeginTabItem("dF/F##dFF"))
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
                                        ImGui.Spacing();

                                        ImGui.TextUnformatted("Background threshold:");
                                        ImGui.SetNextItemWidth(-1f);
                                        double bgThreshMin = 0, bgThreshMax = 255;
                                        ImGui.SliderScalar("##background_threshold", ImGuiDataType.Double, &backgroundThreshold, &bgThreshMin, &bgThreshMax, "%.1f", ImGuiSliderFlags.AlwaysClamp);
                                        ImGui.Spacing();

                                        ImGui.TextUnformatted("Sigma (px):");
                                        ImGui.SetNextItemWidth(-1f);
                                        if (ImGui.InputInt("##sigma", ref sigma))
                                            sigma = Math.Max(0, sigma);

                                        layout = layout with { ImageExpanded = RenderExpandCollapseButton(imageAreaHeight, layout.ImageExpanded) };
                                    }
                                    EndControlColumn();

                                    ImGui.EndTabItem();
                                }

                                if (ImGui.BeginTabItem("Max Projection##MaxProjection"))
                                {
                                    activeTab = ImageTab.MaxProjection;
                                    RenderImageArea("##image_area_maxprojection", imageAreaSize, displaySize, ActiveImage);
                                    ImGui.SameLine();
                                    if (BeginControlColumn("##image_controls_maxprojection", controlColumnWidth, imageAreaHeight))
                                    {
                                        ImGui.TextUnformatted("Max pixel-value projection");
                                        ImGui.Spacing();

                                        if (ImGui.Button("Reset##maxprojection_reset", new Vector2(-1f, 0f)))
                                            resetMaxProjection = true;

                                        layout = layout with { ImageExpanded = RenderExpandCollapseButton(imageAreaHeight, layout.ImageExpanded) };
                                    }
                                    EndControlColumn();

                                    ImGui.EndTabItem();
                                }

                                if (ImGui.BeginTabItem("Reference Image##reference_image"))
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
                                                    Title = "Choose a screenshot to load.",
                                                },
                                                (dlg) => (dlg as OpenFileDialog).FileName);
                                            }
                                        }

                                        if (overlayDialogTask != null && overlayDialogTask.IsCompleted)
                                        {
                                            var chosen = overlayDialogTask.Result;
                                            if (!string.IsNullOrEmpty(chosen))
                                                overlayReferencePath = chosen;
                                            overlayDialogTask = null;
                                        }

                                        ImGui.SameLine();
                                        if (ImGui.Button($"{browseLabel}##browse_screenshots", new Vector2(browseWidth, 0)))
                                        {
                                            var dir = FileDialogHelpers.GetDirectory(DataPath);
                                            if (Directory.Exists(dir))
                                                System.Diagnostics.Process.Start("explorer.exe", dir);
                                        }

                                        if (string.IsNullOrEmpty(overlayReferencePath)) ImGui.BeginDisabled();

                                        ImGui.Checkbox("Apply Live Overlay", ref applyOverlay);

                                        if (string.IsNullOrEmpty(overlayReferencePath)) ImGui.EndDisabled();

                                        layout = layout with { ImageExpanded = RenderExpandCollapseButton(imageAreaHeight, layout.ImageExpanded) };
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
                                if (ImGui.BeginTabBar("##SignalTabBar"))
                                {
                                    ImPlotAxisFlags axisFlags = ImPlotAxisFlags.AutoFit | ImPlotAxisFlags.NoMenus | ImPlotAxisFlags.NoTickMarks | ImPlotAxisFlags.NoGridLines | ImPlotAxisFlags.NoTickLabels;

                                    if (ImGui.BeginTabItem("Quaternion"))
                                    {
                                        var controlsHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ScrollbarSize;
                                        if (ImGui.BeginChild("##quat_controls", new Vector2(0f, controlsHeight), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
                                        {
                                            PlotBufferSizeControl(ref bufferSize);

                                            quaternionLegend.DrawSameLine();
                                            digitalInLegend.DrawSameLine();
                                        }

                                        ImGui.EndChild();

                                        if (ImPlot.BeginPlot("##quaternion_series", fillAvailable, plotFlags))
                                        {
                                            ImPlot.SetupAxes("", "", axisFlags, axisFlags);
                                            ImPlot.SetupAxisLimits(ImAxis.Y1, -1.05, 1.05, ImPlotCond.Always);

                                            if (QuaternionSeries != null)
                                            {
                                                for (int i = 0; i < QuaternionSeries.Series.Length; i++)
                                                {
                                                    if (!quaternionLegend.IsVisible(i))
                                                        continue;

                                                    var line = QuaternionSeries.Series[i];
                                                    ImPlot.SetNextLineStyle(quaternionLegend.ColorOf(i));
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
                                                    if (!digitalInLegend.IsVisible(i))
                                                        continue;

                                                    var line = DigitalInSeries.Series[i];
                                                    ImPlot.SetNextFillStyle(digitalInLegend.ColorOf(i));
                                                    ImPlot.PlotDigitalG(digitalInLabels[i], line.Getter, null, DigitalInSeries.Count);
                                                }
                                            }

                                            ImPlot.EndPlot();
                                        }

                                        ImGui.EndTabItem();
                                    }

                                    if (ImGui.BeginTabItem("Euler Angles"))
                                    {
                                        var controlsHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ScrollbarSize;
                                        if (ImGui.BeginChild("##quat_controls", new Vector2(0f, controlsHeight), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
                                        {
                                            PlotBufferSizeControl(ref bufferSize);
                                            eulerAngleLegend.DrawSameLine();
                                        }

                                        ImGui.EndChild();

                                        if (ImPlot.BeginPlot("##euler_angles_series", fillAvailable, plotFlags))
                                        {
                                            ImPlot.SetupAxes("", "", axisFlags, axisFlags);
                                            ImPlot.SetupAxisLimits(ImAxis.Y1, -200.0, 200.0, ImPlotCond.Always);

                                            if (EulerAnglesSeries != null)
                                            {
                                                for (int i = 0; i < EulerAnglesSeries.Series.Length; i++)
                                                {
                                                    if (!eulerAngleLegend.IsVisible(i))
                                                        continue;

                                                    var line = EulerAnglesSeries.Series[i];
                                                    ImPlot.SetNextLineStyle(eulerAngleLegend.ColorOf(i));
                                                    ImPlot.PlotLineG(line.Name, line.Getter, null, EulerAnglesSeries.Count);
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
                            }

                            ImGui.EndChild();
                        }
                    }

                    ImGui.EndChild();

                    var updatedDisplaySettings = new DataDisplaySettings
                    {
                        BufferSize = bufferSize,
                        Saturation = new SaturationSettings { Threshold = satThreshold, Color = new Scalar(satColor.Z * 255, satColor.Y * 255, satColor.X * 255, satColor.W * 255) },
                        Dff = new DffSettings { BackgroundFrames = backgroundFrames, BackgroundThreshold = backgroundThreshold, Sigma = sigma },
                        MaxProjection = new MaxProjectionSettings { Reset = resetMaxProjection },
                        Overlay = new OverlaySettings { Capture = captureScreenshot, ApplyOverlay = applyOverlay, ReferencePath = overlayReferencePath },
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

        if (ImGui.Button(imageExpanded ? "Collapse##image_expand_toggle" : "Expand##image_expand_toggle", new Vector2(-1f, buttonHeight)))
            return !imageExpanded;
        return imageExpanded;
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

    void PlotBufferSizeControl(ref int bufferSize)
    {
        if (AcquisitionStatus) ImGui.BeginDisabled();

        var bufferInputWidth = 60f * UiScale.Current;
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Buffer Size: ");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(bufferInputWidth);
        if (ImGui.InputInt("##statusbar_buffersize", ref bufferSize, 0, 0))
        {
            bufferSize = Math.Max(2, bufferSize);
        }

        if (AcquisitionStatus) ImGui.EndDisabled();
    }
}
