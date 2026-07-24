using Hexa.NET.ImGui;
using System.Numerics;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Helpers for attaching tooltips to the most recent ImGui item.
/// </summary>
/// <remarks>
/// A control that can be disabled uses <see name="allowWhenDisabled"/> so it can still explain why it is unavailable; the
/// state-dependent line should be drawn with <see cref="Note"/> in a distinct color to set it apart from the
/// static description.
/// </remarks>
internal static class Tooltip
{
    /// <summary>
    /// Secondary hint appended to every slider tooltip, describing how to type in an exact value.
    /// </summary>
    public const string SliderEditHint = "Ctrl + click to type an exact value, then press Enter to apply.";

    /// <summary>
    /// Color used for state-dependent tooltip lines (for example, why a control is currently unavailable).
    /// Matches the warning color used elsewhere in the GUI so cautionary text reads consistently.
    /// </summary>
    public static readonly Vector4 NoteColor = Palette.YellowHovered;

    /// <summary>
    /// Begins a tooltip for the previous item when it is hovered, returning <see langword="true"/> while the
    /// tooltip is open. When it returns <see langword="true"/> the caller must add content and call
    /// <see cref="End"/>. Set <paramref name="allowWhenDisabled"/> so a disabled control can still show a
    /// tooltip explaining why it is disabled.
    /// </summary>
    public static bool Begin(bool allowWhenDisabled = false)
    {
        var flags = ImGuiHoveredFlags.ForTooltip;
        if (allowWhenDisabled)
            flags |= ImGuiHoveredFlags.AllowWhenDisabled;

        return ImGui.IsItemHovered(flags) && ImGui.BeginTooltip();
    }

    /// <summary>
    /// Add a line to an existing <see cref="Tooltip"/> without any formatting or spacing.
    /// </summary>
    /// <param name="text">String to be written in the tooltip.</param>
    public static void AddLine(string text)
    {
        ImGui.TextUnformatted(text);
    }

    /// <summary>
    /// Add a line to an existing <see cref="Tooltip"/> that prints the keyboard combination to 
    /// perform a specific action.
    /// </summary>
    /// <param name="keyCombination">String defining the complete key combination (i.e., <c>"Ctrl + R"</c>).</param>
    /// <param name="action">String defining the action that the keyboard shortcut will perform.</param>
    public static void AddKeyboardShortcut(string keyCombination, string action)
    {
        ImGui.TextDisabled($"Press ({keyCombination}) on the keyboard to {action}.");
    }

    /// <summary>Ends a tooltip opened with <see cref="Begin"/>.</summary>
    /// <remarks>
    /// Only needs to be called if <see cref="Begin"/> returns true.
    /// </remarks>
    public static void End() => ImGui.EndTooltip();

    /// <summary>Attaches a plain description tooltip to the previous item.</summary>
    /// <param name="description">The static description of what the control is or does.</param>
    /// <param name="allowWhenDisabled">Whether the tooltip should still show while the control is disabled.</param>
    public static void Describe(string description, bool allowWhenDisabled = false)
    {
        if (Begin(allowWhenDisabled))
        {
            AddLine(description);
            End();
        }
    }

    /// <summary>
    /// Attaches a slider tooltip to the previous item: the description followed by the shared
    /// <see cref="SliderEditHint"/>, dimmed to read as a secondary instruction.
    /// </summary>
    /// <param name="description">The static description of what the slider affects.</param>
    public static void Slider(string description)
    {
        if (Begin())
        {
            AddLine(description);
            ImGui.Spacing();
            ImGui.TextDisabled(SliderEditHint);
            End();
        }
    }

    /// <summary>
    /// Renders a state-dependent line inside an open tooltip, highlighted in <see cref="NoteColor"/> to set it
    /// apart from the static description above it.
    /// </summary>
    /// <param name="text">The state-dependent text (for example, why a control is currently unavailable).</param>
    public static void Note(string text)
    {
        using (Palette.PushColor(ImGuiCol.Text, NoteColor))
            AddLine(text);
    }
}
