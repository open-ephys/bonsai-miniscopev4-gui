using Bonsai;
using System;
using System.ComponentModel;
using System.Reactive.Linq;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Updates the frame number stamped onto subsequent <see cref="MiniscopeLog"/> messages, then forwards each
/// frame number unchanged.
/// </summary>
[Combinator]
[Description("Updates the frame number associated with subsequent log messages and forwards it unchanged.")]
public class UpdateLogFrameNumber
{
    /// <summary>
    /// Records each frame number as the current log frame number and forwards the sequence unchanged.
    /// </summary>
    /// <param name="source">A sequence of (frameNumber, log) pairs, the frame number typically selected from the acquired frame stream.</param>
    /// <returns>The unmodified <paramref name="source"/> sequence.</returns>
    public IObservable<Tuple<int, MiniscopeLog>> Process(IObservable<Tuple<int, MiniscopeLog>> source)
    {
        return source.Do(value => value.Item2.SetFrameNumber(value.Item1));
    }
}
