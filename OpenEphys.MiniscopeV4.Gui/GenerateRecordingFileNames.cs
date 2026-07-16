using Bonsai;
using Bonsai.IO;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reactive.Linq;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// A synchronized set of file names generated for a single recording. All names share an identical base
/// name and path suffix and differ only by file extension.
/// </summary>
/// <param name="CsvFileName">The file name used to save the per-frame metadata CSV file.</param>
/// <param name="ImageFileName">The file name used to save the recorded video file.</param>
/// <param name="LogFileName">The file name used to save the recording log file.</param>
/// <param name="ConfigFileName">The file name used to save the configuration file.</param>
public readonly record struct RecordingFileNames(string CsvFileName, string ImageFileName, string LogFileName, string ConfigFileName);

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

    const string CsvExtension = ".csv";
    const string ImageExtension = ".avi";
    const string LogExtension = ".log";
    const string ConfigExtension = ".yaml";

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
        string fileName = string.IsNullOrEmpty(FileName) ? "." : FileName;
        var folderPath = Path.GetDirectoryName(fileName);
        var baseFileName = Path.GetFileNameWithoutExtension(fileName);
        var basePath = Path.Combine(folderPath, baseFileName);

        return Suffix switch
        {
            PathSuffix.Timestamp => GenerateTimestampedFileNames(basePath, CsvExtension, ImageExtension, LogExtension, ConfigExtension),
            PathSuffix.FileCount => GenerateCountedFileNames(basePath, CsvExtension, ImageExtension, LogExtension, ConfigExtension),
            _ => new RecordingFileNames(
                Compose(basePath, CsvExtension),
                Compose(basePath, ImageExtension),
                Compose(basePath, LogExtension),
                Compose(basePath, ConfigExtension))
        };
    }

    static string Compose(string basePath, string extension)
    {
        return basePath + extension;
    }

    static string Compose(string basePath, DateTimeOffset timestamp, string extension)
    {
        var path = basePath + extension;
        return PathHelper.AppendTimestamp(path, timestamp);
    }

    static string Compose(string basePath, int countSuffix, string extension)
    {
        var path = basePath + extension;
        return PathHelper.AppendSuffix(path, countSuffix.ToString(CultureInfo.InvariantCulture));
    }

    RecordingFileNames GenerateTimestampedFileNames(string basePath, string csvExtension, string imageExtension, string logExtension, string configExtension)
    {
        var timestamp = DateTimeOffset.Now;
        return new RecordingFileNames(
            Compose(basePath, timestamp, csvExtension),
            Compose(basePath, timestamp, imageExtension),
            Compose(basePath, timestamp, logExtension),
            Compose(basePath, timestamp, configExtension));
    }

    RecordingFileNames GenerateCountedFileNames(string basePath, string csvExtension, string imageExtension, string logExtension, string configExtension)
    {
        var countSuffix = ResolveFileCount(basePath, new[] { csvExtension, imageExtension, logExtension, configExtension });
        return new RecordingFileNames(
            Compose(basePath, countSuffix, csvExtension),
            Compose(basePath, countSuffix, imageExtension),
            Compose(basePath, countSuffix, logExtension),
            Compose(basePath, countSuffix, configExtension));
    }

    // Computes a single file count shared by every extension, so the generated names cannot collide with
    // any existing file and always share the same index. Mirrors PathHelper.AppendFileCount, but takes the
    // maximum count across extensions rather than counting a single one in isolation.
    static int ResolveFileCount(string basePath, string[] extensions)
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

            while (File.Exists(Compose(basePath, matches, extension)))
            {
                matches++;
            }

            if (matches > count) count = matches;
        }

        return count;
    }
}
