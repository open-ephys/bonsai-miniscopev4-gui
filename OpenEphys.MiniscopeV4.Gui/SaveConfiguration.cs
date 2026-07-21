using Bonsai;
using Bonsai.IO;
using System;
using System.ComponentModel;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Forms;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// On each save request from a <see cref="ConfigurationRequest"/>, opens a Save File dialog and writes the
/// current <see cref="MiniscopeConfiguration"/> to the chosen YAML file.
/// </summary>
[Combinator]
[Description("On each save request, opens a Save File dialog and writes the configuration to the chosen YAML file.")]
public class SaveConfiguration
{
    /// <summary>
    /// Writes <see cref="MiniscopeConfiguration"/> to a user-chosen YAML file.
    /// </summary>
    /// <param name="source">A sequence pairing each <see cref="ConfigurationRequest"/> with the <see cref="MiniscopeConfiguration"/> to write; save requests trigger a write.</param>
    /// <param name="logSource">A sequence of miniscope log instances.</param>
    /// <returns>A sequence of the paths the configuration was written to.</returns>
    public IObservable<string> Process(IObservable<Tuple<ConfigurationRequest, MiniscopeConfiguration>> source, IObservable<MiniscopeLog> logSource)
    {
        return Observable.Create<string>(observer =>
        {
            // NB: Expect this to be a BehaviorSubject, so we can take the first value immediately.
            MiniscopeLog log = null;
            var logSubscription = logSource.Take(1).Subscribe(value => log = value);

            if (log == null)
            {
                throw new InvalidOperationException("No MiniscopeLog instance was provided.");
            }

            var sourceSubscription = source
                .Where(value => value.Item1.RequestType == ConfigurationRequestType.ManualSave || value.Item1.RequestType == ConfigurationRequestType.AutoSave)
                .Select(value =>
                {
                    if (!string.IsNullOrEmpty(value.Item1.ConfigFilePath))
                        return Tuple.Create(value.Item1.ConfigFilePath, value.Item2);

                    var task = FileDialogHelpers.RunDialogTask(
                        () => new SaveFileDialog
                        {
                            Filter = "YAML files (*.yml;*.yaml)|*.yml;*.yaml|All Files|*.*",
                            Title = "Choose where to save the configuration.",
                            AddExtension = true,
                            DefaultExt = "yml",
                            FileName = "config.yml",
                            OverwritePrompt = true,
                        },
                        dlg => ((SaveFileDialog)dlg).FileName);

                    var path = task.GetAwaiter().GetResult();
                    return Tuple.Create(path, value.Item2);
                })
                .Where(tuple => !string.IsNullOrEmpty(tuple.Item1))
                .Select(tuple =>
                {
                    var path = tuple.Item1;
                    var config = tuple.Item2;
                    try
                    {
                        PathHelper.EnsureDirectory(path);
                        File.WriteAllText(path, ConfigurationYaml.Serialize(config));
                        log.Info($"Configuration saved to {path}");
                    }
                    catch (Exception ex)
                    {
                        log.Error($"Failed to write configuration to {path}: {ex.Message}");
                    }

                    return path;
                })
                .SubscribeSafe(observer);

            return new CompositeDisposable(logSubscription, sourceSubscription);
        });
    }
}
