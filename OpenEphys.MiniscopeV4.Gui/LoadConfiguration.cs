using System;
using System.ComponentModel;
using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Forms;
using Bonsai;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// On each load request from a <see cref="ConfigurationRequest"/>, opens an Open File dialog and
/// deserializes the chosen YAML file into a <see cref="MiniscopeConfiguration"/>. Downstream, split the
/// result into the individual settings subjects.
/// </summary>
[Combinator]
[Description("On each load request, opens an Open File dialog and deserializes the chosen YAML file into a MiniscopeConfiguration.")]
public class LoadConfiguration
{
    /// <summary>
    /// Emits a <see cref="MiniscopeConfiguration"/> read from a user-chosen YAML file each time
    /// <paramref name="source"/> raises a load request. Files that cannot be read or parsed are skipped.
    /// </summary>
    /// <param name="source">A sequence of <see cref="ConfigurationRequest"/> values; load requests trigger a read.</param>
    /// <param name="logSource">A sequence of miniscope log instances.</param>
    /// <returns>A sequence of loaded <see cref="MiniscopeConfiguration"/> objects.</returns>
    public IObservable<MiniscopeConfiguration> Process(IObservable<ConfigurationRequest> source, IObservable<MiniscopeLog> logSource)
    {
        return Observable.Create<MiniscopeConfiguration>(observer =>
        {
            // NB: Expect this to be a BehaviorSubject, so we can take the first value immediately.
            MiniscopeLog log = null;
            var logSubscription = logSource.Take(1).Subscribe(value => log = value);

            if (log == null)
            {
                throw new InvalidOperationException("No MiniscopeLog instance was provided.");
            }

            var sourceSubscription = source
                .Where(value => value.RequestType == ConfigurationRequestType.ManualLoad || value.RequestType == ConfigurationRequestType.AutoLoad)
                .Select(value =>
                {
                    if (!string.IsNullOrEmpty(value.ConfigFilePath))
                        return value;

                    var task = FileDialogHelpers.RunDialogTask(
                       () => new OpenFileDialog
                       {
                           Filter = "YAML files (*.yml;*.yaml)|*.yml;*.yaml|All Files|*.*",
                           Title = "Choose a configuration to load.",
                           CheckFileExists = true,
                           Multiselect = false,
                       },
                       dlg => ((OpenFileDialog)dlg).FileName);

                    var path = task.GetAwaiter().GetResult();
                    return new ConfigurationRequest(value)
                    {
                        ConfigFilePath = path,
                    };
                })
                .Where(value => !string.IsNullOrEmpty(value.ConfigFilePath))
                .Select(value =>
                {
                    try
                    {
                        var miniscopeConfig = ConfigurationYaml.Deserialize<MiniscopeConfiguration>(File.ReadAllText(value.ConfigFilePath));

                        if (value.RequestType == ConfigurationRequestType.ManualLoad)
                        {
                            log.Info($"Loaded configuration from {value.ConfigFilePath}");
                        }
                        return miniscopeConfig;
                    }
                    catch (Exception ex)
                    {
                        if (value.RequestType == ConfigurationRequestType.ManualLoad)
                            log.Error($"Failed to load configuration from {value.ConfigFilePath}: {ex.Message}");

                        if (value.RequestType == ConfigurationRequestType.AutoLoad)
                            log.Warning($"Unable to load saved configuration file. Using default values instead: {ex.Message}");
                        return null;
                    }
                })
                .Where(config => config != null)
                .SubscribeSafe(observer);

            return new CompositeDisposable(logSubscription, sourceSubscription);
        });
    }
}
