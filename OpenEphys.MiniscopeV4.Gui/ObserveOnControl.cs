using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Observes the source sequence on the shared <see cref="ControlScheduler"/>, moving every
/// downstream notification onto the single Miniscope control thread.
/// </summary>
[Combinator]
[Description("Serializes downstream notifications onto the shared Miniscope control thread, off the GUI render thread.")]
public class ObserveOnControl
{
    /// <summary>
    /// Observes the <paramref name="source"/> sequence on the shared <see cref="ControlScheduler"/>.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
    /// <param name="source">The sequence whose notifications are marshaled onto the control thread.</param>
    /// <returns>The source sequence whose observer callbacks run on the control thread.</returns>
    public IObservable<TSource> Process<TSource>(IObservable<TSource> source)
    {
        return source.ObserveOn(ControlScheduler.Instance);
    }
}
