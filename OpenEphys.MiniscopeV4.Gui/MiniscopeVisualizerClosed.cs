using System;
using System.ComponentModel;
using System.Reactive;
using Bonsai;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Emits a notification when the Miniscope GUI visualizer window is closed.
/// </summary>
[Combinator(MethodName = nameof(Generate))]
[WorkflowElementCategory(ElementCategory.Source)]
[Description("Emits a notification when the Miniscope GUI visualizer window is closed.")]
public class MiniscopeVisualizerClosed
{
    /// <summary>
    /// Generates an observable sequence that emits a notification when the
    /// Miniscope GUI visualizer window is closed.
    /// </summary>
    /// <returns>An observable sequence emitting a <see cref="Unit"/> value on window close.</returns>
    public IObservable<Unit> Generate() => ImGuiMiniscopeVisualizer.Closed;
}
