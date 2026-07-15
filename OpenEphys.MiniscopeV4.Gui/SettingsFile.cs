using System;
using System.IO;
using System.Linq;
using Bonsai.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Reads and writes settings objects to a flat JSON file, so the GUI can export the current configuration
/// and load it back later. Works with any settings type (for example <see cref="MiniscopeSettings"/>).
/// </summary>
/// <remarks>
/// Values are serialized directly: JSON keys match the object's property names and enums are written as
/// their member names (for example <c>"FrameRate": "Fps30"</c>). Only the keys owned by the settings type
/// are touched; any other keys in an existing file (such as the launcher's <c>config.json</c>) are preserved
/// on save and ignored on load. All failures are reported to <see cref="MiniscopeLog"/> and never thrown, so
/// a bad file can never interrupt the render loop.
/// </remarks>
public static class SettingsFile
{
    static readonly JsonSerializer Serializer = CreateSerializer();

    static JsonSerializer CreateSerializer()
    {
        var serializer = new JsonSerializer { Formatting = Formatting.Indented };
        serializer.Converters.Add(new StringEnumConverter());
        return serializer;
    }

    /// <summary>
    /// Writes <paramref name="settings"/> to <paramref name="path"/> as JSON. If the file already exists and
    /// parses as a JSON object, only the keys owned by <typeparamref name="T"/> are overwritten and every
    /// other key is preserved; otherwise a new file containing just those keys is written.
    /// </summary>
    /// <typeparam name="T">The settings type to serialize.</typeparam>
    /// <param name="path">The path of the JSON file to write.</param>
    /// <param name="settings">The settings to save.</param>
    /// <param name="log">The <see cref="MiniscopeLog"/> to report to.</param>
    /// <returns><see langword="true"/> if the file was written; otherwise <see langword="false"/>.</returns>
    public static bool Save<T>(string path, T settings, MiniscopeLog log)
    {
        if (string.IsNullOrEmpty(path))
        {
            log.Error("Could not save configuration: no file path was specified.");
            return false;
        }

        try
        {
            var root = ReadExistingObject(path) ?? new JObject();
            var values = JObject.FromObject(settings, Serializer);
            foreach (var property in values.Properties())
                root[property.Name] = property.Value.DeepClone();

            PathHelper.EnsureDirectory(path);
            File.WriteAllText(path, root.ToString(Formatting.Indented));
            log.Info($"Saved configuration to '{path}'.");
            return true;
        }
        catch (Exception ex)
        {
            log.Error($"Could not save configuration to '{path}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Loads settings from <paramref name="path"/>, falling back to <paramref name="current"/> for any key
    /// that is missing or unparseable. Keys not owned by <typeparamref name="T"/> are ignored.
    /// </summary>
    /// <typeparam name="T">The settings type to deserialize.</typeparam>
    /// <param name="path">The path of the JSON file to read.</param>
    /// <param name="current">The current settings, used as a fallback for missing or invalid values.</param>
    /// <param name="log">The <see cref="MiniscopeLog"/> to report to.</param>
    /// <param name="loaded">The resulting settings on success, or <paramref name="current"/> on failure.</param>
    /// <returns><see langword="true"/> if the file was read; otherwise <see langword="false"/>.</returns>
    public static bool TryLoad<T>(string path, T current, MiniscopeLog log, out T loaded)
    {
        loaded = current;

        if (string.IsNullOrEmpty(path))
        {
            log.Error("Could not load configuration: no file path was specified.");
            return false;
        }

        try
        {
            var fileObject = JObject.Parse(File.ReadAllText(path));

            var merged = JObject.FromObject(current, Serializer);
            foreach (var property in merged.Properties().ToList())
            {
                var token = fileObject[property.Name];
                if (token == null || token.Type == JTokenType.Null)
                    continue;

                var targetType = typeof(T).GetProperty(property.Name)?.PropertyType;
                if (targetType == null)
                    continue;

                try
                {
                    token.ToObject(targetType, Serializer);
                    property.Value = token.DeepClone();
                }
                catch (Exception ex)
                {
                    log.Warning($"Ignoring configuration value for '{property.Name}': {ex.Message}");
                }
            }

            loaded = merged.ToObject<T>(Serializer);
            log.Info($"Loaded configuration from '{path}'.");
            return true;
        }
        catch (Exception ex)
        {
            log.Error($"Could not load configuration from '{path}': {ex.Message}");
            loaded = current;
            return false;
        }
    }

    static JObject ReadExistingObject(string path)
    {
        if (!File.Exists(path))
            return null;

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return JObject.Parse(text);
    }
}
