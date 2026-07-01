using Bonsai;
using System;
using System.ComponentModel;
using System.Reactive.Linq;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Logs the information provided.
/// </summary>
[Combinator]
[Description("Logs the information provided.")]
public class LogInfo
{
    /// <summary>
    /// Records each message in the console log and forwards the sequence unchanged.
    /// </summary>
    /// <param name="source">A sequence of messages.</param>
    /// <returns>The unmodified <paramref name="source"/> sequence.</returns>
    public IObservable<string> Process(IObservable<string> source)
    {
        return source.Do(message => MiniscopeLog.Info(message));
    }
}
