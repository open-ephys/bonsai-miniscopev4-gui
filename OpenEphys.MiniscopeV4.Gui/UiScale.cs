using System;
using Hexa.NET.ImGui;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Shared DPI scale factor applied across the ImGui interface. The scale is derived once from the host
/// control's monitor DPI when its window is created (see <see cref="ImGuiMiniscopeControl"/>) and is
/// read by the font loader, the style, and the panels when sizing pixel-based layout values.
/// </summary>
static class UiScale
{
    /// <summary>The unscaled font size, in pixels, used at 96 DPI (100% scaling).</summary>
    public const float BaseFontSize = 16f;

    const float BaseChildRounding = 4f;
    const float BaseChildBorderSize = 1.5f;

    /// <summary>
    /// Gets the current UI scale factor, where 1.0 corresponds to 96 DPI (100% scaling).
    /// </summary>
    public static float Current { get; private set; } = 1f;

    /// <summary>
    /// Updates <see cref="Current"/> from the specified monitor DPI.
    /// </summary>
    /// <param name="dpi">The per-monitor DPI reported by the host control.</param>
    public static void SetFromDpi(int dpi)
    {
        if (dpi > 0)
            Current = Math.Max(1f, dpi / 96f);
    }

    /// <summary>
    /// Applies the current scale to the ImGui style.
    /// </summary>
    public static void ApplyScaledStyle()
    {
        var style = ImGui.GetStyle();
        style.ScaleAllSizes(Current);
        style.TabBarBorderSize = 0f;
        style.ChildRounding = BaseChildRounding * Current;
        style.ChildBorderSize = BaseChildBorderSize * Current;
    }
}
