using Hexa.NET.ImGui;
using System;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Shared layout state for the bottom console panel. The console height is edited by the
/// <see cref="ConsolePanel"/> splitter and read by the panels above it (the settings sidebar
/// and the data panel) so they can reserve room for the console at the bottom of the window.
/// Mirrors the process-wide static pattern used by <see cref="MiniscopeLog"/>.
/// </summary>
static class ConsoleLayout
{
    /// <summary>Thickness, in pixels, of the draggable splitter above the console.</summary>
    public const float SplitterThickness = 6f;

    const float MinHeight = 65f;

    static float consoleHeight = 130f;

    /// <summary>
    /// Gets or sets the height, in pixels, of the console panel. Values are clamped to a sane range.
    /// </summary>
    public static float ConsoleHeight
    {
        get => consoleHeight;
        set => consoleHeight = Math.Max(MinHeight, Math.Min(ImGui.GetWindowHeight() * 0.4f, value));
    }

    /// <summary>
    /// Gets the total vertical space, in pixels, that panels above the console must leave free
    /// so the splitter and console fit beneath them.
    /// </summary>
    /// <param name="itemSpacingY">The current ImGui vertical item spacing.</param>
    /// <returns>The height to reserve at the bottom of the content region.</returns>
    public static float ReservedHeight(float itemSpacingY) => consoleHeight + SplitterThickness + itemSpacingY * 2f;
}
