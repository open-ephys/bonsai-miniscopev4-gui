using Bonsai.ImGui.Design;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reactive;
using System.Reactive.Subjects;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Represents a visualizer builder that exposes a render notification sequence used to coordinate
/// rendering between a mashup visualizer and its nested visualizers.
/// </summary>
public class ImGuiMiniscopeMashupVisualizerBuilder : ImGuiMashupVisualizerBuilder
{
    internal readonly Subject<Unit> _Render = new();

    /// <inheritdoc/>
    public override Expression Build(IEnumerable<Expression> arguments)
    {
        return Expression.Convert(Expression.Constant(_Render), typeof(IObservable<Unit>));
    }
}
