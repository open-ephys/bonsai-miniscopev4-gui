using YamlDotNet.Serialization;

namespace OpenEphys.MiniscopeV4.Gui;

partial class CommutatorSettings
{
    /// <summary>
    /// Whether the commutator serial port is currently open.
    /// </summary>
    /// <remarks>
    /// Run-time state that is not persisted to the settings file.
    /// </remarks>
    [YamlIgnore]
    public bool IsConnected { get; set; }
}