using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;
using Hexa.NET.ImGui;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Calls <see cref="ImGui.SameLine()"/> so that the next rendered item is placed on the same line as the previous one.
/// </summary>
/// <remarks>
/// Skipped while <see cref="GuiLayout.ImageExpanded"/> is set: the settings sidebar renders nothing at all
/// in that state, so calling SameLine() would attach the next panel to whatever was last rendered before
/// the sidebar instead (typically the status bar), producing a degenerate position/size for it.
/// </remarks>
[Combinator]
[Description("Calls ImGui.SameLine() so that the next rendered item is placed on the same line as the previous one.")]
public class SameLine
{
    /// <summary>
    /// Calls <see cref="ImGui.SameLine()"/> for each layout value (unless the image pane is expanded) and forwards it unchanged.
    /// </summary>
    /// <param name="source">The sequence of shared layout values threaded through the render tick of DearImGui.</param>
    /// <returns>The unmodified <paramref name="source"/> sequence.</returns>
    public IObservable<GuiLayout> Process(IObservable<GuiLayout> source)
    {
        return source.Do(layout =>
        {
            if (!layout.ImageExpanded)
                ImGui.SameLine();
        });
    }
}
