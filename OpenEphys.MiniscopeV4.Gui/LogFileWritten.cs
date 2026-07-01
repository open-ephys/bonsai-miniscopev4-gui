using Bonsai;
using System;
using System.ComponentModel;
using System.IO;
using System.Reactive.Linq;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Logs the absolute path of each incoming file path to the shared <see cref="MiniscopeLog"/> and
/// forwards the path unchanged.
/// </summary>
[Combinator]
[Description("Logs the absolute path of each file being written to the shared console log and forwards it unchanged.")]
public class LogFileWritten
{
    /// <summary>
    /// Records each emitted path in the console log and forwards the sequence unchanged.
    /// </summary>
    /// <param name="source">A sequence of file paths.</param>
    /// <returns>The unmodified <paramref name="source"/> sequence.</returns>
    public IObservable<string> Process(IObservable<string> source)
    {
        return source.Do(path => MiniscopeLog.Log(LogLevel.Info, $"Started recording file template: {Path.GetFullPath(string.IsNullOrEmpty(path) ? "." : path)}"));
    }
}
