using System;
using Hexa.NET.ImGui;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Shared DPI scale factor applied across the ImGui interface. The scale is derived from the host
/// control's monitor DPI (see <see cref="ImGuiMiniscopeControl"/>) and is read by the font loader,
/// the style, and the panels when sizing pixel-based layout values.
/// </summary>
static class UiScale
{
    public const float BaseFontSize = 16f;

    const float BaseChildRounding = 4f;
    const float BaseChildBorderSize = 1.5f;

    /// <summary>
    /// Gets the current UI scale factor, where 1.0 corresponds to 96 DPI (100% scaling).
    /// </summary>
    public static float Current { get; private set; } = 1f;

    static float atlasScale = 1f;
    static float appliedStyleScale = 1f;

    /// <summary>
    /// Updates <see cref="Current"/> from the specified device DPI. Non-positive values are ignored.
    /// </summary>
    /// <param name="deviceDpi">The per-monitor DPI reported by the host control.</param>
    public static void SetFromDpi(int deviceDpi)
    {
        if (deviceDpi <= 0)
            return;

        Current = Math.Max(1f, deviceDpi / 96f);
    }

    /// <summary>
    /// Bakes the current scale into the default ImGui style once, immediately after the font atlas has
    /// been built. Must be called while the ImGui context is current.
    /// </summary>
    public static void ApplyBaselineStyle()
    {
        var style = ImGui.GetStyle();
        style.ScaleAllSizes(Current);
        ApplyCustomStyle(style);

        atlasScale = Current;
        appliedStyleScale = Current;
    }

    /// <summary>
    /// Re-scales the style and font when the DPI has changed since the last applied scale.
    /// </summary>
    internal static void SyncFrame()
    {
        if (Current == appliedStyleScale)
            return;

        var style = ImGui.GetStyle();
        style.ScaleAllSizes(Current / appliedStyleScale);
        ApplyCustomStyle(style);

        style.FontScaleMain = Current / atlasScale;

        appliedStyleScale = Current;
    }

    static void ApplyCustomStyle(ImGuiStylePtr style)
    {
        style.TabBarBorderSize = 0f;
        style.ChildRounding = BaseChildRounding * Current;
        style.ChildBorderSize = BaseChildBorderSize * Current;
    }
}
