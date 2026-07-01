using System;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Bonsai;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Directs the shared <see cref="MiniscopeLog"/> to mirror its messages to a plain text log file for as long
/// as the sequence is subscribed. Opening and closing the file brackets a recording: the file is created when
/// the sequence is subscribed and closed when it is unsubscribed, so every message logged in between — including
/// any error that stops the recording — is captured for the user to look back on.
/// </summary>
[Combinator]
[Description("Mirrors the shared console log to a plain text .log file while the sequence is subscribed (i.e. while recording).")]
[WorkflowElementCategory(ElementCategory.Sink)]
public class WriteLogsToFile
{
    /// <summary>
    /// Gets or sets the path of the log file to write.
    /// </summary>
    [Description("The path of the log file to write.")]
    [Editor("Bonsai.Design.SaveFileNameEditor, Bonsai.Design", DesignTypes.UITypeEditor)]
    public string FileName { get; set; }

    /// <summary>
    /// Opens the log file when <paramref name="source"/> is subscribed and closes it when unsubscribed,
    /// forwarding the elements unchanged.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the <paramref name="source"/> sequence.</typeparam>
    /// <param name="source">The sequence whose subscription lifetime defines the recording window.</param>
    /// <returns>An observable sequence identical to <paramref name="source"/>.</returns>
    public IObservable<TSource> Process<TSource>(IObservable<TSource> source)
    {
        return Observable.Create<TSource>(observer =>
        {
            MiniscopeLog.StartFile(FileName);
            var subscription = source.SubscribeSafe(observer);
            return Disposable.Create(() =>
            {
                subscription.Dispose();
                MiniscopeLog.StopFile();
            });
        });
    }
}
