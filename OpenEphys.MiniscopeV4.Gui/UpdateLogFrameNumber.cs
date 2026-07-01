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
    /// <param name="source">A sequence of frame numbers, typically selected from the acquired frame stream.</param>
    /// <returns>The unmodified <paramref name="source"/> sequence.</returns>
    public IObservable<int> Process(IObservable<int> source)
    {
        return source.Do(frameNumber => MiniscopeLog.SetFrameNumber(frameNumber));
    }
}
