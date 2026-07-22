using Bonsai;
using System;
using System.Reactive.Linq;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Specifies the type of configuration request being made.
/// </summary>
public enum ConfigurationRequestType
{
    /// <summary>
    /// Specifies no request.
    /// </summary>
    None,
    /// <summary>
    /// Specifies a manual save request.
    /// </summary>
    ManualSave,
    /// <summary>
    /// Specifies a manual load request.
    /// </summary>
    ManualLoad,
    /// <summary>
    /// Specifies an automatic save request.
    /// </summary>
    AutoSave,
    /// <summary>
    /// Specifies an automatic load request.
    /// </summary>
    AutoLoad
}

/// <summary>
/// Represents a request to save or load the GUI configuration.
/// </summary>
[WorkflowElementCategory(ElementCategory.Source)]
[Combinator(MethodName = "Generate")]
public class ConfigurationRequest
{
    /// <summary>
    /// Gets or sets the type of configuration request being made.
    /// </summary>
    public ConfigurationRequestType RequestType { get; set; }

    /// <summary>
    /// Gets or sets the configuration file path.
    /// </summary>
    public string ConfigFilePath { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationRequest"/> class with default values.
    /// </summary>
    public ConfigurationRequest()
    {
        RequestType = ConfigurationRequestType.None;
        ConfigFilePath = "";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationRequest"/> class
    /// by copying the values from another instance.
    /// </summary>
    /// <param name="other">The instance to copy values from.</param>
    public ConfigurationRequest(ConfigurationRequest other)
    {
        RequestType = other.RequestType;
        ConfigFilePath = other.ConfigFilePath;
    }

    /// <summary>
    /// Generates an observable sequence that emits a new <see cref="ConfigurationRequest"/> instance
    /// </summary>
    /// <typeparam name="TSource">The type of the elements in the source sequence.</typeparam>
    /// <param name="source">The source sequence.</param>
    /// <returns>An observable sequence that emits a new <see cref="ConfigurationRequest"/> instance.</returns>
    public IObservable<ConfigurationRequest> Generate<TSource>(IObservable<TSource> source)
    {
        return Observable.Select(source, _ => new ConfigurationRequest(this));
    }

    /// <summary>
    /// Generates an observable sequence that emits a new <see cref="ConfigurationRequest"/> instance
    /// </summary>
    /// <returns>An observable sequence that emits a new <see cref="ConfigurationRequest"/> instance.</returns>
    public IObservable<ConfigurationRequest> Generate()
    {
        return Observable.Defer(() => Observable.Return(new ConfigurationRequest(this)));
    }
}
