using System;
using System.Collections.Concurrent;
using System.Threading;

namespace OpenEphys.Miniscope.Design.Gui;

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
}

/// <summary>
/// A single timestamped message in the console log.
/// </summary>
/// <param name="Timestamp">The time the message was recorded.</param>
/// <param name="Level">The severity of the message.</param>
/// <param name="Message">The message text.</param>
public readonly record struct LogEntry(DateTime Timestamp, LogLevel Level, string Message);

/// <summary>
/// A process-wide, thread-safe queue that accumulates console messages (errors, written file paths,
/// and other notable actions) for display in the GUI. Any producer can push to it directly, and the
/// console panel reads <see cref="Snapshot"/> to render the scrollback history.
/// </summary>
public static class MiniscopeLog
{
    static readonly ConcurrentQueue<LogEntry> entries = new();
    static int version;

    /// <summary>
    /// Gets a monotonically increasing counter that changes whenever the log is modified. Renderers
    /// can compare against a cached value to avoid taking a <see cref="Snapshot"/> every frame.
    /// </summary>
    public static int Version => Volatile.Read(ref version);

    /// <summary>
    /// Appends a message to the log. Empty messages are ignored.
    /// </summary>
    /// <param name="level">The severity of the message.</param>
    /// <param name="message">The message text.</param>
    public static void Log(LogLevel level, string message)
    {
        if (string.IsNullOrEmpty(message))
            return;

        entries.Enqueue(new LogEntry(DateTime.Now, level, message));
        Interlocked.Increment(ref version);
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
}
