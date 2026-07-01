using System.Reactive.Concurrency;
using System.Threading;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Provides the single, process-wide scheduler used to serialize every control-plane state
/// update in the GUI onto one dedicated thread.
/// </summary>
public static class ControlScheduler
{
    /// <summary>
    /// The shared control scheduler. Backed by a single <see cref="EventLoopScheduler"/>, so every
    /// action scheduled on it runs sequentially on the same dedicated background thread.
    /// </summary>
    public static readonly EventLoopScheduler Instance = new(start => new Thread(start)
    {
        Name = "MiniscopeControl",
        IsBackground = true,
    });
}
