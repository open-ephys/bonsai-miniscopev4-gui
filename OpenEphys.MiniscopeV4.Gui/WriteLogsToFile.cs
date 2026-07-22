using Bonsai;
using System;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Directs the <see cref="MiniscopeLog"/> instance supplied on the second input to mirror its messages to a
/// CSV log file for as long as the sequence is subscribed. Opening and closing the file brackets a recording:
/// the file is created when the sequence is subscribed and closed when it is unsubscribed, so every message
/// logged in between — including any error that stops the recording — is captured for the user to look back on.
/// </summary>
[Combinator]
[Description("Mirrors the shared console log to a CSV file while the sequence is subscribed (i.e. while recording).")]
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
    /// Captures the <see cref="MiniscopeLog"/> instance from <paramref name="logSource"/>, opens its log file
    /// when <paramref name="source"/> is subscribed, and closes it when unsubscribed, forwarding the elements
    /// unchanged.
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the <paramref name="source"/> sequence.</typeparam>
    /// <param name="source">The sequence whose subscription lifetime defines the recording window.</param>
    /// <param name="logSource">
    /// The shared log instance (typically a <c>BehaviorSubject</c>) whose file is opened for the duration of the
    /// recording. The first emitted instance is cached for the lifetime of the subscription.
    /// </param>
    /// <returns>An observable sequence identical to <paramref name="source"/>.</returns>
    public IObservable<TSource> Process<TSource>(IObservable<TSource> source, IObservable<MiniscopeLog> logSource)
    {
        return Observable.Create<TSource>(observer =>
        {
            MiniscopeLog log = null;

            // NB: Expect this to be a BehaviorSubject, so we can take the first value immediately.
            var logSubscription = logSource.Take(1).Subscribe(value =>
            {
                log = value;
                log.StartFile(FileName);
            });

            if (log == null)
            {
                throw new InvalidOperationException("No MiniscopeLog instance was provided.");
            }

            var subscription = source.SubscribeSafe(observer);
            return Disposable.Create(() =>
            {
                subscription.Dispose();
                log.StopFile();
                logSubscription.Dispose();
            });
        });
    }
}
