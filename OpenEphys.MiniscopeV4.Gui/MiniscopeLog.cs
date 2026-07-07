using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading;
using Bonsai.IO;

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
/// A process-wide, thread-safe queue that accumulates console messages (errors, written file paths,
/// and other notable actions) for display in the GUI. Any producer can push to it directly, and the
/// console panel reads <see cref="Snapshot"/> to render the scrollback history.
/// </summary>
/// <remarks>
/// While a recording is active the log can additionally mirror every message to a CSV file. Call
/// <see cref="StartFile"/> when a recording begins and <see cref="StopFile"/> when it ends; in between, any
/// message passed to <see cref="Log"/> is appended to the file as well as the in-memory queue.
/// </remarks>
public static class MiniscopeLog
{
    const string FileTimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
    const char Delimiter = ',';

    static readonly string DelimiterText = Delimiter.ToString();
    static readonly char[] QuoteRequiredCharacters = { Delimiter, '"', '\n', '\r' };
    static readonly ConcurrentQueue<LogEntry> entries = new();
    static readonly object fileGate = new();
    static StreamWriter fileWriter;
    static int currentFrameNumber;
    static int version;

    /// <summary>
    /// Gets a monotonically increasing counter that changes whenever the log is modified. Renderers
    /// can compare against a cached value to avoid taking a <see cref="Snapshot"/> every frame.
    /// </summary>
    public static int Version => Volatile.Read(ref version);

    /// <summary>
    /// Appends a message to the log. Empty messages are ignored. If a log file is currently open (see
    /// <see cref="StartFile"/>), the message is also written to it.
    /// </summary>
    /// <param name="level">The severity of the message.</param>
    /// <param name="message">The message text.</param>
    public static void Log(LogLevel level, string message)
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
    public static void Info(string message) => Log(LogLevel.Info, message);

    /// <summary>Appends a warning message.</summary>
    /// <param name="message">The message text.</param>
    public static void Warning(string message) => Log(LogLevel.Warning, message);

    /// <summary>Appends an error message.</summary>
    /// <param name="message">The message text.</param>
    public static void Error(string message) => Log(LogLevel.Error, message);

    /// <summary> Appends a message indicating that a property has changed.</summary>
    /// <param name="message">The message text.</param>
    public static void PropertyChanged(string message) => Log(LogLevel.PropertyChanged, message);

    /// <summary>
    /// Sets the frame number recorded with subsequent messages. Typically driven by the acquired frame stream.
    /// </summary>
    /// <param name="frameNumber">The current frame number.</param>
    internal static void SetFrameNumber(int frameNumber) => Volatile.Write(ref currentFrameNumber, frameNumber);

    /// <summary>
    /// Returns the current log contents in chronological order (oldest first).
    /// </summary>
    /// <returns>A copy of the buffered <see cref="LogEntry"/> values.</returns>
    public static LogEntry[] Snapshot() => entries.ToArray();

    /// <summary>
    /// Removes all messages from the log.
    /// </summary>
    public static void Clear()
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
    /// Only a single log file can be open at a time: the log owns one process-wide writer, so overlapping
    /// recordings are not supported. Starting a new file while one is already open replaces it and logs a
    /// warning, since that indicates a previous recording was not stopped cleanly. A failure to open the file
    /// is reported to the log but does not throw, so it never interrupts a recording.
    /// </remarks>
    /// <param name="path">The path of the CSV log file to write.</param>
    internal static void StartFile(string path)
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
    internal static void StopFile()
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

    static void WriteToFile(LogEntry entry)
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
    static void CloseFile()
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
