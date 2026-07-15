using System;
using System.ComponentModel;
using System.IO;
using System.Reactive.Linq;
using Bonsai;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Writes each received settings object to a JSON file using <see cref="SettingsFile"/>, forwarding the
/// elements unchanged.
/// </summary>
/// <remarks>
/// Serialization follows <see cref="SettingsFile.Save{T}"/>: only the keys owned by the element type are
/// written, any other keys already in the file are preserved, and failures are reported to
/// <see cref="MiniscopeLog"/> rather than thrown, so a write error never interrupts the sequence.
/// </remarks>
[Combinator]
[Description("Writes each received settings object to a JSON file.")]
[WorkflowElementCategory(ElementCategory.Sink)]
public class WriteSettingsToFile
{
    /// <summary>
    /// Gets or sets the path of the JSON file to write.
    /// </summary>
    [Description("The path of the JSON file to write.")]
    [Editor("Bonsai.Design.SaveFileNameEditor, Bonsai.Design", DesignTypes.UITypeEditor)]
    public string FileName { get; set; }

    /// <summary>
    /// Writes each element of <paramref name="source"/> to the JSON file at <see cref="FileName"/>,
    /// forwarding the elements unchanged.
    /// </summary>
    /// <typeparam name="TSource">The type of the settings object to serialize.</typeparam>
    /// <param name="source">A sequence of (settings, log) pairs; each settings object is written and failures are reported to the paired log.</param>
    /// <returns>An observable sequence identical to <paramref name="source"/>.</returns>
    public IObservable<Tuple<TSource, MiniscopeLog>> Process<TSource>(IObservable<Tuple<TSource, MiniscopeLog>> source)
    {
        return source.Do(entry =>
        {
            var (value, log) = entry;
            if (!File.Exists(FileName))
                SettingsFile.Save(FileName, value, log);

            else
                log.Error($"The file '{FileName}' already exists.");
        });
    }
}
