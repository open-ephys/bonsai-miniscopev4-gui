using Bonsai;
using Hexa.NET.ImGui;
using Newtonsoft.Json.Linq;
using OpenEphys.Miniscope;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Renders all settings panels in a collapsible sidebar and returns an updated <see cref="HardwareSettings"/>.
/// </summary>
/// <remarks>
/// Opens the shared sidebar child window but does not close it, so that <see cref="FilePanel"/> can render
/// into the same region immediately afterward and close it. The two coordinate through the threaded
/// <see cref="GuiLayout"/>.
/// </remarks>
[Combinator]
[Description("Renders all settings panels in a collapsible left-hand sidebar.")]
public class SettingsPanel
{
    /// <summary>
    /// Gets or sets the acquisition status of the GUI.
    /// </summary>
    [Browsable(false)]
    public bool AcquisitionStatus { get; set; }

    /// <summary>
    /// Gets or sets the recording status of the GUI.
    /// </summary>
    [Browsable(false)]
    public bool RecordingStatus { get; set; }

    static float ExpandedWidth => 375f * UiScale.Current;
    static float CollapsedWidth => 36f * UiScale.Current;

    bool settingsOpen = true;

    bool EffectiveOpen(bool imageExpanded) => settingsOpen && !imageExpanded;

    float GetCurrentWidth(float availableX, bool imageExpanded)
    {
        if (EffectiveOpen(imageExpanded))
        {
            if (ExpandedWidth <= availableX)
                return ExpandedWidth;

            else
            {
                settingsOpen = false;
                return availableX;
            }
        }

        return CollapsedWidth;
    }

    static readonly string[] SensorGainValues = Enum.GetNames(typeof(GainV4)); // TODO: This is sorted by backing value => High | Low | Medium.
    static readonly string[] FrameRateValues = Enum.GetNames(typeof(FrameRateV4)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    static readonly string[] DigitalInNames = Enum.GetNames(typeof(MiniscopeDaqDigitalIn));
    static readonly MiniscopeDaqDigitalIn[] DigitalInValues = (MiniscopeDaqDigitalIn[])Enum.GetValues(typeof(MiniscopeDaqDigitalIn));

    /// <summary>
    /// Renders the settings sidebar and returns an updated <see cref="HardwareSettings"/> alongside the shared layout.
    /// </summary>
    /// <param name="source">A sequence pairing the shared <see cref="GuiLayout"/> with the current <see cref="HardwareSettings"/>, tied to the render tick of DearImGui.</param>
    /// <param name="logSource">The shared <see cref="MiniscopeLog"/> instance (typically a <c>BehaviorSubject</c>), captured once.</param>
    /// <returns>A sequence pairing the updated <see cref="GuiLayout"/> with the settings updated from the rendered controls.</returns>
    public unsafe IObservable<Tuple<GuiLayout, HardwareSettings, ConfigurationRequest>> Process(IObservable<Tuple<GuiLayout, HardwareSettings>> source, IObservable<MiniscopeLog> logSource)
    {
        return Observable.Create<Tuple<GuiLayout, HardwareSettings, ConfigurationRequest>>(observer =>
        {
            List<string> portNames = new();
            List<string> pendingPortNames = null;
            bool initialScanStarted = false;
            bool wasAcquiring = false;

            Task<string> exportFileTask = null;
            Task<string> importFileTask = null;
            List<Task<Tuple<string, bool>>> querySerialPorts = new();

            void StartPortScan()
            {
                if (querySerialPorts.Count > 0) return;

                pendingPortNames = SerialPort.GetPortNames().ToList();

                foreach (var port in pendingPortNames)
                {
                    querySerialPorts.Add(QuerySerialPort(port));
                }
            }

            // NB: Expect these to be BehaviorSubjects, so we can take the first value immediately.
            MiniscopeLog log = null;
            var logSubscription = logSource.Take(1).Subscribe(value => log = value);

            if (log == null)
            {
                throw new InvalidOperationException("No MiniscopeLog instance was provided.");
            }

            var sourceObserver = Observer.Create<Tuple<GuiLayout, HardwareSettings>>(value =>
            {
                var (layout, hardwareSettings) = value;
                bool imageExpanded = layout.ImageExpanded;

                if (!initialScanStarted)
                {
                    initialScanStarted = true;
                    StartPortScan();
                }

                double ledBrightness = hardwareSettings.Miniscope.LedBrightness;
                double focus = hardwareSettings.Miniscope.Focus;
                GainV4 sensorGain = hardwareSettings.Miniscope.SensorGain;
                FrameRateV4 frameRate = hardwareSettings.Miniscope.FrameRate;
                MiniscopeDaqDigitalIn ledRespectsDigitalIn = hardwareSettings.Miniscope.LedRespectsDigitalIn;

                string portName = hardwareSettings.Commutator.PortName;
                bool commutatorConnected = hardwareSettings.Commutator.IsConnected;
                bool commutatorEnable = hardwareSettings.Commutator.Enable;
                bool commutatorEnableLed = hardwareSettings.Commutator.EnableLed;
                bool commutatorAutoConnect = hardwareSettings.Commutator.AutoConnect;

                ConfigurationRequestType requestType = ConfigurationRequestType.None;
                string configFilePath = string.Empty;

                bool validPort = !string.IsNullOrEmpty(portName) && portNames.Contains(portName);

                if (imageExpanded)
                {
                    // Fully hidden while the image section is expanded (matching the console's own
                    // fully-hidden behavior) — the only way out is the Collapse button in DataPanel.
                    // No child is opened at all here (SameLine and FilePanel both skip their half of
                    // this row/child in this state too), so there's nothing to close and DataPanel
                    // isn't pinned next to a degenerate zero-width remnant.
                    layout = layout with { SidebarOpen = false };
                }
                else
                {
                    float availableX = ImGui.GetContentRegionAvail().X;
                    float panelWidth = GetCurrentWidth(availableX, imageExpanded);

                    float consoleReserve = layout.ReservedConsoleHeight(ImGui.GetStyle().ItemSpacing.Y);
                    ImGui.BeginChild("##settings_pane", new Vector2(panelWidth - ImGui.GetStyle().ChildBorderSize, -consoleReserve), ImGuiChildFlags.Borders);

                    layout = layout with { SidebarOpen = EffectiveOpen(imageExpanded) };

                    if (!EffectiveOpen(imageExpanded))
                    {
                        // The arrow icon only occupies a small square, but the whole collapsed column should
                        // act as one big reopen button: an invisible button behind it catches clicks/hover
                        // anywhere in the column, and the real ArrowButton drawn on top is tinted to look
                        // hovered whenever that larger area is, even if the mouse isn't precisely on the icon.
                        Vector2 collapsedAreaSize = ImGui.GetContentRegionAvail();
                        Vector2 arrowPos = ImGui.GetCursorScreenPos();

                        bool areaClicked = ImGui.InvisibleButton("##settings_open_area", collapsedAreaSize);
                        bool areaHovered = ImGui.IsItemHovered();
                        if (areaHovered)
                            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                        ImGui.SetCursorScreenPos(arrowPos);

                        if (areaHovered)
                            ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonHovered));

                        ImGui.ArrowButton("##settings_open", ImGuiDir.Right);
                        Tooltip.Describe("Expand the control panel.");

                        if (areaHovered)
                            ImGui.PopStyleColor();

                        if (areaClicked)
                        {
                            settingsOpen = true;
                        }
                    }
                    else
                    {
                        if (ImGui.ArrowButton("##settings_close", ImGuiDir.Left))
                            settingsOpen = false;
                        Tooltip.Describe("Collapse the control panel.");

                        ImGui.SameLine();
                        ImGui.Text("Control Panel");

                        string exportButtonString = "Export##miniscope_save";
                        string importButtonString = "Import##miniscope_load";
                        var exportButtonWidth = ImGui.CalcTextSize(exportButtonString, true).X + ImGui.GetStyle().FramePadding.X * 2.0f;
                        var importButtonWidth = ImGui.CalcTextSize(importButtonString, true).X + ImGui.GetStyle().FramePadding.X * 2.0f;
                        var buttonGap = ImGui.GetStyle().ItemSpacing.X * 2.0f;

                        ImGui.SameLine();
                        var avail = ImGui.GetContentRegionAvail().X;
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - exportButtonWidth - buttonGap - importButtonWidth);
                        if (ImGui.Button(exportButtonString, new Vector2(exportButtonWidth, 0f)))
                        {
                            exportFileTask = FileDialogHelpers.RunDialogTask(
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
                        }
                        Tooltip.Describe(
                            "Export all current settings to a configuration file (saved as\n" +
                            $"a *{GenerateRecordingFileNames.ConfigExtension} file).");

                        if (exportFileTask != null && exportFileTask.IsCompleted)
                        {
                            var result = exportFileTask.Result;

                            if (!string.IsNullOrEmpty(result))
                            {
                                requestType = ConfigurationRequestType.ManualSave;
                                configFilePath = result;
                            }

                            exportFileTask = null;
                        }

                        ImGui.SameLine(0, ImGui.GetStyle().ItemSpacing.X);
                        if (ImGui.Button(importButtonString, new Vector2(importButtonWidth, 0f)))
                        {
                            importFileTask = FileDialogHelpers.RunDialogTask(
                               () => new OpenFileDialog
                               {
                                   Filter = "YAML files (*.yml;*.yaml)|*.yml;*.yaml|All Files|*.*",
                                   Title = "Choose a configuration to load.",
                                   CheckFileExists = true,
                                   Multiselect = false,
                               },
                               dlg => ((OpenFileDialog)dlg).FileName);
                        }
                        Tooltip.Describe($"Import all settings from an existing *{GenerateRecordingFileNames.ConfigExtension} configuration file.");

                        if (importFileTask != null && importFileTask.IsCompleted)
                        {
                            var result = importFileTask.Result;

                            if (!string.IsNullOrEmpty(result))
                            {
                                requestType = ConfigurationRequestType.ManualLoad;
                                configFilePath = result;
                            }

                            importFileTask = null;
                        }

                        ImGui.Spacing();
                        ImGui.Separator();
                        ImGui.Spacing();

                        ImGui.BeginChild("##settings_content", new Vector2(-1f, -layout.RecordingSectionHeight), ImGuiChildFlags.None);

                        ImGui.SetNextItemOpen(true, ImGuiCond.Once);
                        bool open = ImGui.CollapsingHeader("Miniscope##miniscope_header");
                        Tooltip.Describe(
                            "Click to show or hide Miniscope settings. All settings are\n" +
                            "saved in a configuration file when recording starts. Any changes\n" +
                            "to these settings are recorded in the log for tracking.");
                        if (open)
                        {
                            ImGui.BeginChild("##miniscope_group", new Vector2(0f, 0f), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY);

                            ImGui.AlignTextToFramePadding();
                            ImGui.Text("Focus: ");
                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(-1f);
                            double focusMin = -100, focusMax = 100;
                            ImGui.SliderScalar("##focus", ImGuiDataType.Double, &focus, &focusMin, &focusMax, "%.1f", ImGuiSliderFlags.AlwaysClamp);
                            Tooltip.Slider(
                                "Adjust the focus of the electrowetting lens (EWL) around its\n" +
                                $"nominal focal plane ({focusMin}% to {focusMax}%).\n" +
                                "Note that the relationship between this percentage and the resulting\n" +
                                "focal plane adjustment is non-linear.");

                            ImGui.AlignTextToFramePadding();
                            ImGui.Text("LED Brightness: ");
                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(-1f);
                            double brightnessMin = 0, brightnessMax = 100;
                            ImGui.SliderScalar("##ledbrightness", ImGuiDataType.Double, &ledBrightness, &brightnessMin, &brightnessMax, "%.1f", ImGuiSliderFlags.AlwaysClamp);
                            Tooltip.Slider(
                                "Set the excitation LED level as a percentage of the maximum current [0% to 100%].\n" +
                                "Note that the relationship between the excitation level and radiance is non-linear.");

                            if (ImGui.BeginTable("##row2", 2, ImGuiTableFlags.SizingStretchSame))
                            {
                                ImGui.TableNextColumn();
                                ImGui.AlignTextToFramePadding();
                                ImGui.Text("Frame Rate: ");
                                ImGui.SameLine();
                                ImGui.SetNextItemWidth(-1f);
                                int frameRateIndex = Array.IndexOf(FrameRateValues, frameRate.ToString());

                                if (RecordingStatus) ImGui.BeginDisabled();
                                if (ImGui.Combo("##framerate", ref frameRateIndex, FrameRateValues, FrameRateValues.Length))
                                {
                                    if (Enum.TryParse<FrameRateV4>(FrameRateValues[frameRateIndex], out var result))
                                        frameRate = result;
                                }
                                if (Tooltip.Begin())
                                {
                                    Tooltip.AddLine("Select the number of frames the Miniscope acquires per second.");
                                    if (RecordingStatus)
                                        Tooltip.Note("Unavailable while recording.");
                                    Tooltip.End();
                                }
                                if (RecordingStatus) ImGui.EndDisabled();

                                ImGui.TableNextColumn();
                                ImGui.AlignTextToFramePadding();
                                ImGui.Text("Sensor Gain: ");
                                ImGui.SameLine();
                                ImGui.SetNextItemWidth(-1f);
                                int sensorGainIndex = Array.IndexOf(SensorGainValues, sensorGain.ToString());
                                if (ImGui.Combo("##sensorgain", ref sensorGainIndex, SensorGainValues, SensorGainValues.Length))
                                {
                                    if (Enum.TryParse<GainV4>(SensorGainValues[sensorGainIndex], out var result))
                                        sensorGain = result;
                                }
                                Tooltip.Describe("Select the image sensor's analog gain.");

                                ImGui.EndTable();
                            }

                            ImGui.AlignTextToFramePadding();
                            ImGui.Text("LED Trigger: ");
                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(-1f);
                            int digitalInIndex = Array.IndexOf(DigitalInValues, ledRespectsDigitalIn);
                            if (ImGui.Combo("##ledrespectdigitalin", ref digitalInIndex, DigitalInNames, DigitalInNames.Length))
                            {
                                ledRespectsDigitalIn = DigitalInValues[digitalInIndex];
                            }
                            Tooltip.Describe(
                                "Select whether or not to gate the excitation LED with a digital input.\n" +
                                "The LED turns on only while the selected digital input is high.\n" +
                                "If set to None, the LED will be on continuously.");

                            ImGui.Spacing();
                            ImGui.Separator();
                            ImGui.Spacing();
                            ImGui.TextDisabled("Status: ");
                            ImGui.SameLine();
                            if (AcquisitionStatus)
                            {
                                using (Palette.PushColor(ImGuiCol.Text, Palette.GreenHovered))
                                    ImGui.Text("Acquiring");
                            }
                            else
                            {
                                ImGui.TextDisabled("Disconnected");
                            }

                            ImGui.EndChild();
                        }

                        open = ImGui.CollapsingHeader("Commutator##commutator_header");
                        Tooltip.Describe(
                            "Click to show or hide Commutator settings. The commutator refers to a\n" +
                            "torque-free commutator that enables tangle-free connection between\n" +
                            "a stationary data acquisition device and the Miniscope on a freely-\n" +
                            "moving animal. This device is not required to operate the Miniscope.");
                        if (open)
                        {
                            ImGui.BeginChild("##commutator_group", new Vector2(0f, 0f), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY);

                            ImGui.AlignTextToFramePadding();
                            ImGui.Text("Commutator: ");
                            ImGui.SameLine();

                            var style = ImGui.GetStyle();
                            float refreshButtonWidth = ImGui.CalcTextSize("Searching").X + style.FramePadding.X * 2;
                            float comboWidth = ImGui.GetContentRegionAvail().X - refreshButtonWidth - style.ItemSpacing.X;
                            ImGui.SetNextItemWidth(comboWidth);

                            for (int i = querySerialPorts.Count - 1; i >= 0; i--)
                            {
                                var task = querySerialPorts[i];
                                if (!task.IsCompleted)
                                    continue;

                                if (task.Status == TaskStatus.RanToCompletion)
                                {
                                    var result = task.Result;
                                    if (!result.Item2)
                                    {
                                        pendingPortNames.Remove(result.Item1);
                                    }
                                }

                                querySerialPorts.RemoveAt(i);
                            }

                            bool searching = querySerialPorts.Count > 0;

                            if (pendingPortNames != null && !searching)
                            {
                                portNames = pendingPortNames;
                                portNames.Sort();
                                pendingPortNames = null;
                            }

                            bool commutatorsFound = portNames.Count > 0;

                            if (AcquisitionStatus && !wasAcquiring && commutatorAutoConnect && !commutatorConnected)
                            {
                                if (validPort)
                                    commutatorConnected = true;

                                else if (commutatorsFound)
                                {
                                    log.Warning($"{nameof(hardwareSettings.Commutator.AutoConnect)} is enabled but no commutator was connected; the selected COM port '{portName}' is not valid.");
                                }
                            }
                            wasAcquiring = AcquisitionStatus;

                            int portIndex = portNames.IndexOf(portName);
                            if (!searching && portIndex < 0)
                            {
                                if (commutatorsFound)
                                {
                                    portIndex = 0;
                                    portName = portNames[0];
                                }
                                else if (!string.IsNullOrEmpty(portName))
                                {
                                    portName = "";
                                }
                            }

                            if (commutatorConnected || searching) ImGui.BeginDisabled();

                            if (ImGui.BeginCombo("##comport", commutatorsFound ? portNames[portIndex] : "No commutator found"))
                            {
                                for (int i = 0; i < portNames.Count; i++)
                                {
                                    bool isSelected = (i == portIndex);
                                    if (ImGui.Selectable(portNames[i], isSelected))
                                    {
                                        portIndex = i;
                                        portName = portNames[i];
                                    }
                                    if (isSelected)
                                        ImGui.SetItemDefaultFocus();
                                }
                                ImGui.EndCombo();
                            }
                            if (Tooltip.Begin(allowWhenDisabled: true))
                            {
                                Tooltip.AddLine(
                                    "Select the commutator's COM port, which is the serial connection Windows assigns\n" +
                                    "to the USB cable linking it to this computer (e.g., COM1).");
                                if (commutatorConnected)
                                    Tooltip.Note("Unavailable while a commutator is connected.");
                                if (searching)
                                    Tooltip.Note("Unavailable while searching for connected commutators.");
                                Tooltip.End();
                            }

                            ImGui.SameLine();
                            if (ImGui.Button(searching ? "Searching##comrefresh" : "Refresh##comrefresh"))
                            {
                                StartPortScan();
                            }
                            if (Tooltip.Begin(allowWhenDisabled: true))
                            {
                                Tooltip.AddLine("Search for connected commutators.");
                                if (commutatorConnected)
                                    Tooltip.Note("Unavailable while the commutator is connected.");
                                if (searching)
                                    Tooltip.Note("Unavailable while searching for connected commutators.");
                                Tooltip.End();
                            }

                            if (searching || commutatorConnected) ImGui.EndDisabled();

                            if (!commutatorsFound) ImGui.BeginDisabled();

                            if (ImGui.BeginTable("##commutator_connect", 2, ImGuiTableFlags.SizingStretchSame))
                            {
                                ImGui.TableNextColumn();
                                ImGui.Checkbox("Auto Connect##commutator_autoconnect", ref commutatorAutoConnect);

                                if (Tooltip.Begin())
                                {
                                    Tooltip.AddLine("Automatically connect the commutator when acquisition starts, if a commutator is selected.");

                                    if (!commutatorsFound)
                                        Tooltip.Note("Unavailable while no commutators are found.");

                                    Tooltip.End();
                                }

                                ImGui.TableNextColumn();
                                using (Palette.PushButtonColors(
                                    commutatorConnected ? Palette.Red : Palette.Green,
                                    commutatorConnected ? Palette.RedHovered : Palette.GreenHovered,
                                    commutatorConnected ? Palette.RedActive : Palette.GreenActive))
                                {
                                    if (ImGui.Button(commutatorConnected ? "Disconnect##combutton" : "Connect##combutton", new Vector2(-1f, 0f)))
                                    {
                                        commutatorConnected = !commutatorConnected;
                                    }
                                }
                                if (Tooltip.Begin())
                                {
                                    Tooltip.AddLine(commutatorConnected
                                        ? "Disconnect from the commutator."
                                        : "Connect to the selected commutator.");

                                    if (!commutatorsFound)
                                        Tooltip.Note("Unavailable while no commutators are found.");

                                    Tooltip.End();
                                }

                                ImGui.EndTable();
                            }

                            if (!commutatorsFound) ImGui.EndDisabled();

                            ImGui.Separator();

                            if (!commutatorConnected) ImGui.BeginDisabled();

                            if (ImGui.BeginTable("##commutator_checkboxes", 2, ImGuiTableFlags.SizingStretchSame))
                            {
                                ImGui.TableNextColumn();
                                ImGui.Checkbox("Enable##commutator_enable", ref commutatorEnable);
                                if (Tooltip.Begin(allowWhenDisabled: true))
                                {
                                    Tooltip.AddLine("Enable the commutator motor so it counteracts cable twist as the animal rotates.");
                                    if (!commutatorConnected)
                                        Tooltip.Note("Unavailable while the commutator is disconnected.");
                                    Tooltip.End();
                                }

                                ImGui.TableNextColumn();
                                ImGui.Checkbox("Enable LED##commutator_led", ref commutatorEnableLed);
                                if (Tooltip.Begin(allowWhenDisabled: true))
                                {
                                    Tooltip.AddLine("Turn on the commutator's indicator LED.");
                                    if (!commutatorConnected)
                                        Tooltip.Note("Unavailable while the commutator is disconnected.");
                                    Tooltip.End();
                                }

                                ImGui.EndTable();
                            }

                            if (!commutatorConnected)
                                ImGui.EndDisabled();

                            ImGui.Separator();

                            ImGui.TextDisabled("Status: ");
                            ImGui.SameLine();
                            if (commutatorConnected)
                            {
                                using (Palette.PushColor(ImGuiCol.Text, Palette.GreenHovered))
                                    ImGui.Text("Connected");
                            }
                            else
                            {
                                ImGui.TextDisabled("Disconnected");
                            }

                            ImGui.EndChild();
                        }

                        ImGui.EndChild();
                    }
                }

                var updatedHardwareSettings = new HardwareSettings
                {
                    Miniscope = new MiniscopeSettings
                    {
                        LedBrightness = ledBrightness,
                        Focus = focus,
                        SensorGain = sensorGain,
                        FrameRate = frameRate,
                        LedRespectsDigitalIn = ledRespectsDigitalIn,
                    },
                    Commutator = new CommutatorSettings
                    {
                        PortName = portName,
                        IsConnected = commutatorConnected,
                        Enable = commutatorEnable,
                        EnableLed = commutatorEnableLed,
                        AutoConnect = commutatorAutoConnect,
                    },
                };

                var updatedConfigurationRequest = new ConfigurationRequest
                {
                    RequestType = requestType,
                    ConfigFilePath = configFilePath
                };

                observer.OnNext(Tuple.Create(layout, updatedHardwareSettings, updatedConfigurationRequest));
            },
            observer.OnError,
            observer.OnCompleted);

            return new CompositeDisposable(logSubscription, source.SubscribeSafe(sourceObserver));
        });
    }

    static Task<Tuple<string, bool>> QuerySerialPort(string portName)
    {
        return Task.Run(() =>
        {
            bool result = false;
            Thread t = new(() =>
            {
                using var sp = new SerialPort(portName)
                {
                    ReadTimeout = 1000,
                };
                result = false;

                try
                {
                    sp.Open();
                    sp.Write("{print: true}");

                    string buffer = sp.ReadLine();

                    if (!string.IsNullOrEmpty(buffer))
                    {
                        var json = JObject.Parse(buffer);

                        if (json.TryGetValue("firmware", out var firmware)
                            && json.TryGetValue("enable", out var enable)
                            && json.TryGetValue("led", out var enableLed))
                        {
                            result = true;
                        }
                    }
                }
                catch (Exception) { }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            return Tuple.Create(portName, result);
        });
    }
}
