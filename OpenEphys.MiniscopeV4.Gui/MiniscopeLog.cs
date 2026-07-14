using Bonsai;
using Bonsai.IO;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reactive.Linq;
using System.Threading;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Severity of a <see cref="LogEntry"/>.
/// </summary>
public enum LogLevel
{
    /// <summary>Informational message, such as a file being written.</summary>
    Info,

    /// <summary>A recoverable problem the user should be aware of.</summary>
    Warning,

    /// <summary>An error that interrupted acquisition, recording, or another action.</summary>
    Error,

    /// <summary> A message indicating that a property has changed. </summary>
    PropertyChanged,
}

/// <summary>
/// A single timestamped message in the console log.
/// </summary>
/// <param name="Timestamp">The time the message was recorded.</param>
/// <param name="FrameNumber">The frame number associated with the message.</param>
/// <param name="Level">The severity of the message.</param>
/// <param name="Message">The message text.</param>
public readonly record struct LogEntry(DateTime Timestamp, int FrameNumber, LogLevel Level, string Message);

/// <summary>
/// A thread-safe queue that accumulates console messages (errors, written file paths, and other notable
/// actions) for display in the GUI. A single instance is created per GUI scope and shared through the
/// workflow (via a <c>BehaviorSubject</c>): any producer can push to it directly, and the console panel
/// reads <see cref="Snapshot"/> to render the scrollback history.
/// </summary>
/// <remarks>
/// While a recording is active the log can additionally mirror every message to a CSV file. Call
/// <see cref="StartFile"/> when a recording begins and <see cref="StopFile"/> when it ends; in between, any
/// message passed to <see cref="Log"/> is appended to the file as well as the in-memory queue.
/// </remarks>
public class MiniscopeLog
{
    const string FileTimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
    const char Delimiter = ',';

    static readonly string DelimiterText = Delimiter.ToString();
    static readonly char[] QuoteRequiredCharacters = { Delimiter, '"', '\n', '\r' };

    readonly ConcurrentQueue<LogEntry> entries = new();
    readonly object fileGate = new();

    StreamWriter fileWriter;
    int currentFrameNumber;
    int version;

    /// <summary>
    /// Gets a monotonically increasing counter that changes whenever the log is modified. Renderers
    /// can compare against a cached value to avoid taking a <see cref="Snapshot"/> every frame.
    /// </summary>
    public int Version => Volatile.Read(ref version);

    /// <summary>
    /// Appends a message to the log. Empty messages are ignored. If a log file is currently open (see
    /// <see cref="StartFile"/>), the message is also written to it.
    /// </summary>
    /// <param name="level">The severity of the message.</param>
    /// <param name="message">The message text.</param>
    public void Log(LogLevel level, string message)
    {
        if (string.IsNullOrEmpty(message))
            return;

        var entry = new LogEntry(DateTime.Now, Volatile.Read(ref currentFrameNumber), level, message);
        entries.Enqueue(entry);
        Interlocked.Increment(ref version);
        WriteToFile(entry);
    }

    /// <summary>Appends an informational message.</summary>
    /// <param name="message">The message text.</param>
    public void Info(string message) => Log(LogLevel.Info, message);

    /// <summary>Appends a warning message.</summary>
    /// <param name="message">The message text.</param>
    public void Warning(string message) => Log(LogLevel.Warning, message);

    /// <summary>Appends an error message.</summary>
    /// <param name="message">The message text.</param>
    public void Error(string message) => Log(LogLevel.Error, message);

    /// <summary> Appends a message indicating that a property has changed.</summary>
    /// <param name="message">The message text.</param>
    public void PropertyChanged(string message) => Log(LogLevel.PropertyChanged, message);

    /// <summary>
    /// Sets the frame number recorded with subsequent messages. Typically driven by the acquired frame stream.
    /// </summary>
    /// <param name="frameNumber">The current frame number.</param>
    internal void SetFrameNumber(int frameNumber) => Volatile.Write(ref currentFrameNumber, frameNumber);

    /// <summary>
    /// Returns the current log contents in chronological order (oldest first).
    /// </summary>
    /// <returns>A copy of the buffered <see cref="LogEntry"/> values.</returns>
    public LogEntry[] Snapshot() => entries.ToArray();

    /// <summary>
    /// Removes all messages from the log.
    /// </summary>
    public void Clear()
    {
        while (entries.TryDequeue(out _))
        {
        }

        Interlocked.Increment(ref version);
    }

    /// <summary>
    /// Begins mirroring subsequent messages to the specified CSV file. Any file already open is closed first.
    /// </summary>
    /// <remarks>
    /// Each log instance owns a single writer, so only one file can be open per instance at a time; separate
    /// log instances (e.g. two composed GUIs) record to their own files independently. Starting a new file
    /// while one is already open replaces it and logs a warning, since that indicates a previous recording was
    /// not stopped cleanly. A failure to open the file is reported to the log but does not throw, so it never
    /// interrupts a recording.
    /// </remarks>
    /// <param name="path">The path of the CSV log file to write.</param>
    internal void StartFile(string path)
    {
        Exception failure = null;
        bool replacedOpenFile;
        lock (fileGate)
        {
            replacedOpenFile = fileWriter != null;
            CloseFile();
            try
            {
                if (string.IsNullOrEmpty(path))
                    throw new InvalidOperationException("A valid log file name must be specified.");

                PathHelper.EnsureDirectory(path);
                var stream = new FileStream(path, FileMode.CreateNew);
                fileWriter = new StreamWriter(stream) { AutoFlush = true };
                fileWriter.WriteLine(HeaderRow());
                fileWriter.WriteLine(FormatRow(new LogEntry(DateTime.Now, Volatile.Read(ref currentFrameNumber), LogLevel.Info, "Recording started")));
            }
            catch (Exception ex)
            {
                CloseFile();
                failure = ex;
            }
        }

        if (replacedOpenFile)
            Warning("A previous log file was still open; it has been closed before starting a new one.");

        if (failure != null)
            Error($"Could not open log file '{path}': {failure.Message}");
    }

    /// <summary>
    /// Stops mirroring messages to the log file and closes it. Safe to call when no file is open.
    /// </summary>
    internal void StopFile()
    {
        Exception failure = null;
        lock (fileGate)
        {
            if (fileWriter == null)
                return;

            try { fileWriter.WriteLine(FormatRow(new LogEntry(DateTime.Now, Volatile.Read(ref currentFrameNumber), LogLevel.Info, "Recording stopped"))); }
            catch (Exception ex) { failure = ex; }
            CloseFile();
        }

        if (failure != null)
            Error($"Failed to write to log file: {failure.Message}");
    }

    void WriteToFile(LogEntry entry)
    {
        Exception failure = null;
        lock (fileGate)
        {
            if (fileWriter == null)
                return;

            try
            {
                fileWriter.WriteLine(FormatRow(entry));
            }
            catch (Exception ex)
            {
                CloseFile();
                failure = ex;
            }
        }

        if (failure != null)
            Error($"Failed to write to log file: {failure.Message}");
    }

    static string FormatRow(LogEntry entry)
    {
        return string.Join(
            DelimiterText,
            CsvField(entry.Timestamp.ToString(FileTimestampFormat, CultureInfo.InvariantCulture)),
            CsvField(entry.FrameNumber.ToString(CultureInfo.InvariantCulture)),
            CsvField(entry.Level.ToString()),
            CsvField(entry.Message));
    }

    static string HeaderRow()
    {
        return string.Join(
            DelimiterText,
            CsvField("Timestamp"),
            CsvField("FrameNumber"),
            CsvField("Level"),
            CsvField("Message"));
    }

    // Quotes a field per RFC-4180 if it contains the delimiter, a quote, or a line break; otherwise returns it
    // as-is.
    static string CsvField(string value)
    {
        value ??= string.Empty;
        if (value.IndexOfAny(QuoteRequiredCharacters) >= 0)
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        return value;
    }

    // Disposes the current writer, if any. Callers must hold fileGate. The writer is cleared before any failure
    // is reported, so the Error() call cannot recurse back into a file write.
    void CloseFile()
    {
        if (fileWriter == null)
            return;

        Exception failure = null;
        try { fileWriter.Dispose(); }
        catch (Exception ex) { failure = ex; }
        fileWriter = null;

        if (failure != null)
            Error($"Failed to close log file: {failure.Message}");
    }
}

/// <summary>
/// Creates a new <see cref="MiniscopeLog"/> instance and emits it to the workflow.
/// </summary>
[Description("Emits the shared console log store for this GUI scope.")]
[Combinator]
public class CreateMiniscopeLog
{
    /// <summary>
    /// Creates a new <see cref="MiniscopeLog"/> instance and emits it to the workflow.
    /// </summary>
    /// <returns>a new <see cref="MiniscopeLog"/> instance.</returns>
    public IObservable<MiniscopeLog> Process() => Observable.Return(new MiniscopeLog());
}
