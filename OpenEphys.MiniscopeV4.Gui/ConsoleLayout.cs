using Hexa.NET.ImGui;
using System;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Shared layout state for the bottom console panel. The console height is edited by the
/// <see cref="ConsolePanel"/> splitter and read by the panels above it (the settings sidebar
/// and the data panel) so they can reserve room for the console at the bottom of the window.
/// </summary>
static class ConsoleLayout
{
    /// <summary>Thickness, in pixels, of the draggable splitter above the console. Scales with <see cref="UiScale"/>.</summary>
    public static float SplitterThickness => 6f * UiScale.Current;

    const float BaseMinHeight = 65f;
    const float MaxHeightFraction = 0.4f;

    static float MinHeight => BaseMinHeight * UiScale.Current;

    static float consoleHeight = 130f;

    /// <summary>
    /// Gets or sets whether the console is expanded (showing its log content and splitter) or
    /// collapsed down to just its header row, toggled by the arrow button in <see cref="ConsolePanel"/>.
    /// </summary>
    public static bool ConsoleOpen { get; set; } = true;

    /// <summary>
    /// Gets the console height, in pixels, seeding it from a <see cref="UiScale"/>-scaled default on first
    /// use and clamping it (on every call) to a sane range for the current <paramref name="windowHeight"/>:
    /// never below the scaled minimum, never above a fraction of the window. Clamping on every read means a
    /// window shrink is reflected immediately rather than lingering until the next drag.
    /// </summary>
    /// <param name="windowHeight">The height, in pixels, of the window the console is docked in.</param>
    /// <returns>The clamped console height.</returns>
    public static float GetConsoleHeight(float windowHeight) => consoleHeight = Clamp(consoleHeight, windowHeight);

    /// <summary>
    /// Adjusts the console height by <paramref name="deltaY"/> pixels (positive = grow), clamped to the
    /// same range as <see cref="GetConsoleHeight"/> for the current <paramref name="windowHeight"/>.
    /// </summary>
    /// <param name="deltaY">The change in height, in pixels.</param>
    /// <param name="windowHeight">The height, in pixels, of the window the console is docked in.</param>
    public static void Drag(float deltaY, float windowHeight) =>
        consoleHeight = Clamp(consoleHeight + deltaY, windowHeight);

    static float Clamp(float height, float windowHeight) =>
        Math.Max(MinHeight, Math.Min(windowHeight * MaxHeightFraction, height));

    /// <summary>
    /// Gets the total vertical space, in pixels, that panels above the console must leave free
    /// so the splitter and console fit beneath them. Zero while <see cref="DataPanelLayout.ImageExpanded"/>
    /// is set, since the console is fully hidden in that view; just enough for the header row while
    /// <see cref="ConsoleOpen"/> is false.
    /// </summary>
    /// <param name="itemSpacingY">The current ImGui vertical item spacing.</param>
    /// <returns>The height to reserve at the bottom of the content region.</returns>
    public static float ReservedHeight(float itemSpacingY)
    {
        if (DataPanelLayout.ImageExpanded)
            return 0f;
        if (!ConsoleOpen)
            return ImGui.GetFrameHeight() + ImGui.GetStyle().WindowPadding.Y * 2f + itemSpacingY;
        return GetConsoleHeight(ImGui.GetWindowHeight()) + SplitterThickness + itemSpacingY * 2f;
    }
}
