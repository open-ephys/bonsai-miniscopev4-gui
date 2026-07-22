using Hexa.NET.ImGui;
using System.Numerics;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// A fixed set of named, colored plot lines with runtime visibility toggles, rendered as an inline row
/// of clickable color swatches that replaces an ImPlot legend. Each swatch controls whether the matching
/// line is drawn, mirroring how clicking an ImPlot legend entry shows or hides a series.
/// </summary>
/// <remarks>
/// The number of lines in a given plot never changes at runtime, so the label and color of every line are
/// fixed at construction and can be shared with the plotting code (see <see cref="ColorOf"/>). Visibility is
/// runtime-only session state that is not persisted, matching an ImPlot legend where toggles reset each session.
/// A single tab may combine several legends (for example, orientation and digital-input lines) by drawing them
/// consecutively on the same line.
/// </remarks>
sealed class PlotLegend
{
    /// <summary>A single legend line: a fixed label paired with the color of its swatch and plot line, and its visiblity status.</summary>
    public struct Entry
    {
        /// <summary>Initializes a new <see cref="Entry"/> with the given label and color.</summary>
        /// <param name="label">The text displayed next to the color swatch.</param>
        /// <param name="color">The color of the swatch and of the corresponding plot line.</param>
        /// <param name="visible">Whether the legend entry should be plotted or not.</param>
        public Entry(string label, Vector4 color, bool visible = true)
        {
            Label = label;
            Color = color;
            Visible = visible;
        }

        /// <summary>Gets the text displayed next to the color swatch.</summary>
        public string Label { get; }

        /// <summary>Gets the color of the swatch and of the corresponding plot line.</summary>
        public Vector4 Color { get; }

        /// <summary> Gets or sets whether the legend entry should be plotted or not.</summary>
        public bool Visible { get; set; }
    }

    const float HiddenSwatchAlpha = 0.3f;

    readonly string id;
    readonly Entry[] entries;

    /// <summary>
    /// Initializes a new <see cref="PlotLegend"/> with a stable identifier and its fixed set of lines.
    /// </summary>
    /// <param name="id">A stable identifier used to keep the swatch widgets unique across legends.</param>
    /// <param name="entries">The fixed lines, in the same order as the series they control.</param>
    public PlotLegend(string id, params Entry[] entries)
    {
        this.id = id;
        this.entries = entries;
    }

    /// <summary>Gets the number of lines in the legend.</summary>
    public int Count => entries.Length;

    /// <summary>Gets whether the line at <paramref name="index"/> is currently shown.</summary>
    public bool IsVisible(int index) => index < Count && entries[index].Visible;

    void ToggleVisibility(int index)
    {
        if (index < Count)
            entries[index].Visible = !entries[index].Visible;
    }

    /// <summary>Gets the color of the line at <paramref name="index"/>.</summary>
    public Vector4 ColorOf(int index) => index < Count ? entries[index].Color : Vector4.Zero;

    /// <summary>
    /// Renders the legend as a row of clickable color swatches with labels, each placed on the current line
    /// via <see cref="ImGui.SameLine()"/> so it continues after the preceding item. Clicking a swatch toggles
    /// the corresponding line's visibility; a hidden line's swatch and label are dimmed.
    /// </summary>
    public void DrawSameLine()
    {
        var style = ImGui.GetStyle();
        float rowHeight = ImGui.GetFrameHeight();
        float swatchSize = ImGui.GetTextLineHeight();
        float innerSpacing = style.ItemInnerSpacing.X;
        float rounding = style.FrameRounding * 0.5f;
        var drawList = ImGui.GetWindowDrawList();

        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var textSize = ImGui.CalcTextSize(entry.Label);
            var itemSize = new Vector2(swatchSize + innerSpacing + textSize.X, rowHeight);

            ImGui.SameLine();
            var origin = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton($"##sig_legend_{id}_{i}", itemSize))
                ToggleVisibility(i);

            bool shown = entry.Visible;
            if (ImGui.IsItemHovered())
                drawList.AddRectFilled(origin, origin + itemSize, ImGui.GetColorU32(ImGuiCol.FrameBgHovered), style.FrameRounding);

            var swatchColor = entry.Color;
            if (!shown)
                swatchColor.W *= HiddenSwatchAlpha;

            float swatchTop = origin.Y + (rowHeight - swatchSize) * 0.5f;
            var swatchMin = new Vector2(origin.X, swatchTop);
            var swatchMax = new Vector2(origin.X + swatchSize, swatchTop + swatchSize);
            drawList.AddRectFilled(swatchMin, swatchMax, ImGui.GetColorU32(swatchColor), rounding);
            drawList.AddRect(swatchMin, swatchMax, ImGui.GetColorU32(ImGuiCol.Border), rounding);

            var textPos = new Vector2(origin.X + swatchSize + innerSpacing, origin.Y + (rowHeight - textSize.Y) * 0.5f);
            drawList.AddText(textPos, ImGui.GetColorU32(shown ? ImGuiCol.Text : ImGuiCol.TextDisabled), entry.Label);
        }
    }
}
