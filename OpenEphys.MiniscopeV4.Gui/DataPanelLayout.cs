using System;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Shared layout state for the runtime-only split between the image pane and the signal pane inside
/// <see cref="DataPanel"/>. Unlike <see cref="ConsoleLayout.ConsoleHeight"/> this has no Bonsai-configurable
/// backing property: the height is seeded once from <see cref="DataPanel.ImageHeightFraction"/> times the
/// available height on first use, then purely dragged for the rest of the process's lifetime.
/// </summary>
static class DataPanelLayout
{
    /// <summary>
    /// Gets or sets whether the image pane fills the entire data panel. When set, the settings sidebar
    /// and the console are both fully hidden (see <see cref="SettingsLayout"/> and <see cref="ConsoleLayout"/>),
    /// and the image/signal splitter and signal pane are skipped entirely. Cleared by the Collapse button
    /// alongside the Expand button that sets it.
    /// </summary>
    public static bool ImageExpanded { get; set; }

    /// <summary>Thickness, in pixels, of the draggable splitter between the image and signal panes.</summary>
    public const float SplitterThickness = 6f;

    const float MinImagePaneHeight = 100f;
    const float MinSignalPaneHeight = 80f;

    static float imagePaneHeight = -1f; // Negative = not yet seeded.

    /// <summary>
    /// Gets the current image pane height, seeding it from <paramref name="availableForPanes"/> times
    /// <paramref name="seedFraction"/> on first call, and clamping it (on every call) so both panes retain
    /// their minimum height given the current <paramref name="availableForPanes"/>.
    /// </summary>
    public static float GetImagePaneHeight(float availableForPanes, float seedFraction)
    {
        if (imagePaneHeight < 0f)
            imagePaneHeight = availableForPanes * Math.Max(0f, Math.Min(1f, seedFraction));

        return imagePaneHeight = Clamp(imagePaneHeight, availableForPanes);
    }

    /// <summary>
    /// Adjusts the image pane height by <paramref name="deltaY"/> pixels (positive = grow), clamped so
    /// neither pane shrinks below its minimum height given <paramref name="availableForPanes"/>.
    /// </summary>
    public static void Drag(float deltaY, float availableForPanes) =>
        imagePaneHeight = Clamp(imagePaneHeight + deltaY, availableForPanes);

    static float Clamp(float height, float availableForPanes)
    {
        float maxHeight = Math.Max(MinImagePaneHeight, availableForPanes - MinSignalPaneHeight);
        return Math.Max(MinImagePaneHeight, Math.Min(maxHeight, height));
    }
}
