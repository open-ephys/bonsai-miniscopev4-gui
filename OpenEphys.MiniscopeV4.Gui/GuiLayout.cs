using Bonsai;
using Hexa.NET.ImGui;
using System;
using System.ComponentModel;
using System.Reactive.Linq;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Snapshot of the shared GUI layout state, threaded through the panel chain each render
/// tick. Every panel reads the fields it needs and returns an updated copy, so cross-panel
/// coordination happens through the dataflow graph rather than through mutable static state.
/// </summary>
/// <param name="SidebarOpen">
/// Whether the settings sidebar is currently expanded (showing its full content) rather than
/// collapsed to icon width.
/// </param>
/// <param name="RecordingSectionHeight">
/// The height, in pixels, that the Recording section's content occupied last frame.
/// </param>
/// <param name="ImageExpanded">
/// Whether the image pane fills the entire data panel. When set, the settings sidebar and the
/// console are both fully hidden and the image/signal splitter and signal pane are skipped.
/// </param>
/// <param name="ImagePaneHeight">
/// The dragged pixel height of the image pane inside <see cref="DataPanel"/>.
/// </param>
/// <param name="ConsoleOpen">
/// Whether the console is expanded (showing its log content and splitter) or collapsed to its
/// header row.
/// </param>
/// <param name="ConsoleHeight">
/// The dragged pixel height of the console.
/// </param>
public record GuiLayout(
    bool SidebarOpen,
    float RecordingSectionHeight,
    bool ImageExpanded,
    float ImagePaneHeight,
    bool ConsoleOpen,
    float ConsoleHeight)
{
    const float BaseMinConsoleHeight = 65f;
    const float ConsoleMaxHeightFraction = 0.4f;

    float MinConsoleHeight => BaseMinConsoleHeight * UiScale.Current;

    /// <summary>Creates a layout with the default state used to seed the workflow's Layout subject.</summary>
    public GuiLayout() : this(
        SidebarOpen: false,
        RecordingSectionHeight: 0f,
        ImageExpanded: false,
        ImagePaneHeight: -1f,
        ConsoleOpen: true,
        ConsoleHeight: 130f)
    {
    }

    /// <summary>Thickness, in pixels, of the draggable splitter above the console. Scales with <see cref="UiScale"/>.</summary>
    public float ConsoleSplitterThickness => 6f * UiScale.Current;

    /// <summary>
    /// Clamps <paramref name="height"/> to a sane console height for the given
    /// <paramref name="windowHeight"/>: never below the scaled minimum, never above a fraction of
    /// the window.
    /// </summary>
    public float ClampConsoleHeight(float height, float windowHeight) =>
        Math.Max(MinConsoleHeight, Math.Min(windowHeight * ConsoleMaxHeightFraction, height));

    /// <summary>
    /// Gets the total vertical space, in pixels, that panels above the console must leave free so
    /// the splitter and console fit beneath them. Zero while <see cref="ImageExpanded"/> is set
    /// (the console is fully hidden); just enough for the header row while <see cref="ConsoleOpen"/>
    /// is false.
    /// </summary>
    /// <param name="itemSpacingY">The current ImGui vertical item spacing.</param>
    /// <returns>The height to reserve at the bottom of the content region.</returns>
    public float ReservedConsoleHeight(float itemSpacingY)
    {
        if (ImageExpanded)
            return 0f;
        if (!ConsoleOpen)
            return ImGui.GetFrameHeight() + ImGui.GetStyle().WindowPadding.Y * 2f + itemSpacingY;
        return ClampConsoleHeight(ConsoleHeight, ImGui.GetWindowHeight()) + ConsoleSplitterThickness + itemSpacingY * 2f;
    }
}

/// <summary>
/// Emits the default <see cref="GuiLayout"/>, used to seed the workflow's <c>Layout</c> subject.
/// </summary>
[Description("Emits the default shared GUI layout state.")]
[Combinator]
public class CreateGuiLayout
{
    /// <summary>Creates the default <see cref="GuiLayout"/> as a single-value source sequence.</summary>
    /// <returns>A sequence containing the default <see cref="GuiLayout"/>.</returns>
    public IObservable<GuiLayout> Process()
    {
        return Observable.Return(new GuiLayout());
    }
}
