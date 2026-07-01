using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reactive.Linq;
using Bonsai;
using Bonsai.IO;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// A synchronized set of file names generated for a single recording. All names share an identical base
/// name and path suffix and differ only by file extension.
/// </summary>
/// <param name="CsvFileName">The file name used to save the per-frame metadata CSV file.</param>
/// <param name="ImageFileName">The file name used to save the recorded video file.</param>
/// <param name="LogFileName">The file name used to save the recording log file.</param>
public readonly record struct RecordingFileNames(string CsvFileName, string ImageFileName, string LogFileName);

/// <summary>
/// Generates a synchronized set of file names for a recording from a single file name template and
/// <see cref="PathSuffix"/>, so that the CSV, video, and log files always share an identical base name
/// and suffix.
/// </summary>
/// <remarks>
/// The suffix is resolved <b>once</b> per generated set: for <see cref="PathSuffix.Timestamp"/> a single
/// timestamp is shared by all files, and for <see cref="PathSuffix.FileCount"/> a single count is computed
/// across every extension. This guarantees the files line up, in contrast to letting each writer resolve its
/// own suffix independently (which can diverge for timestamps and drift for file counts). Because the suffix
/// is baked into the returned names, the downstream writers must be configured with <see cref="PathSuffix.None"/>
/// so that it is not applied a second time.
/// </remarks>
[Combinator]
[Description("Generates a synchronized set of CSV, video, and log file names from a file name template and path suffix.")]
[WorkflowElementCategory(ElementCategory.Transform)]
public class GenerateRecordingFileNames
{
    /// <summary>
    /// Gets or sets the file name template used as the base for all generated names.
    /// </summary>
    [Description("The file name template used as the base for all generated names.")]
    [Editor("Bonsai.Design.SaveFileNameEditor, Bonsai.Design", DesignTypes.UITypeEditor)]
    public string FileName { get; set; }

    /// <summary>
    /// Gets or sets the suffix applied to the base name, before each file extension.
    /// </summary>
    [Description("The suffix applied to the base name, before each file extension.")]
    public PathSuffix Suffix { get; set; }

    /// <summary>
    /// Gets or sets the extension used for the metadata CSV file.
    /// </summary>
    [Description("The extension used for the metadata CSV file.")]
    public string CsvExtension { get; set; } = ".csv";

    /// <summary>
    /// Gets or sets the extension used for the recorded video file.
    /// </summary>
    [Description("The extension used for the recorded video file.")]
    public string ImageExtension { get; set; } = ".avi";

    /// <summary>
    /// Gets or sets the extension used for the recording log file.
    /// </summary>
    [Description("The extension used for the recording log file.")]
    public string LogExtension { get; set; } = ".log";

    /// <summary>
    /// Generates a synchronized <see cref="RecordingFileNames"/> set each time the <paramref name="source"/>
    /// sequence produces a notification.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the <paramref name="source"/> sequence.</typeparam>
    /// <param name="source">
    /// The sequence used to trigger generation. A new set of names is resolved for each element, using the
    /// current <see cref="FileName"/> and <see cref="Suffix"/> values, so this is typically driven by a
    /// record-start pulse.
    /// </param>
    /// <returns>A sequence of synchronized file name sets, one for each element of <paramref name="source"/>.</returns>
    public IObservable<RecordingFileNames> Process<TSource>(IObservable<TSource> source)
    {
        return source.Select(_ => Generate());
    }

    /// <summary>
    /// Resolves a synchronized set of file names using the current <see cref="FileName"/> and <see cref="Suffix"/>.
    /// </summary>
    /// <returns>The generated <see cref="RecordingFileNames"/>.</returns>
    RecordingFileNames Generate()
    {
        var basePath = Path.GetFullPath(string.IsNullOrEmpty(FileName) ? "." : FileName);
        var extensions = new[] { CsvExtension, ImageExtension, LogExtension };
        var timestamp = DateTimeOffset.Now;
        var countSuffix = Suffix == PathSuffix.FileCount
            ? ResolveFileCount(basePath, extensions)
            : null;

        return new RecordingFileNames(
            Compose(basePath, CsvExtension, timestamp, countSuffix),
            Compose(basePath, ImageExtension, timestamp, countSuffix),
            Compose(basePath, LogExtension, timestamp, countSuffix));
    }

    string Compose(string basePath, string extension, DateTimeOffset timestamp, string countSuffix)
    {
        var path = basePath + extension;
        return Suffix switch
        {
          PathSuffix.Timestamp => PathHelper.AppendTimestamp(path, timestamp),
          PathSuffix.FileCount => PathHelper.AppendSuffix(path, countSuffix),
          _ => path,
        };
  }

    // Computes a single file count shared by every extension, so the generated names cannot collide with
    // any existing file and always share the same index. Mirrors PathHelper.AppendFileCount, but takes the
    // maximum count across extensions rather than counting a single one in isolation.
    static string ResolveFileCount(string basePath, string[] extensions)
    {
        var count = 0;
        foreach (var extension in extensions)
        {
            var path = basePath + extension;
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) directory = ".";
            if (!Directory.Exists(directory)) continue;

            var fileName = Path.GetFileNameWithoutExtension(path);
            var matches = Directory.GetFiles(directory, fileName + "*" + extension).Length;
            if (matches > count) count = matches;
        }

        return count.ToString(CultureInfo.InvariantCulture);
    }
}
