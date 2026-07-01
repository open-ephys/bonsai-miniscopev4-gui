using System.Collections.Generic;
using Bonsai.ImGui;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Defines a visualizer that initializes an ImGui-based visualizer for Miniscope data.
/// </summary>
public class ImGuiMiniscopeVisualizer : ImGuiMiniscopeMashupVisualizer
{
    /// <inheritdoc/>
    protected override IEnumerable<IExtensionFactory> GetExtensions()
    {
        yield return ImGuiMiniscopeExtension.Factory;
    }
}
