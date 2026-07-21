using Bonsai;
using Hexa.NET.ImGui;
using OpenEphys.Miniscope;
using System;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

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
    public bool AcquisitionStatus { get; set; }

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
            var portNames = SerialPort.GetPortNames();
            bool wasAcquiring = false;

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

                bool validPort = !string.IsNullOrEmpty(portName) && portNames.Contains(portName);
                if (AcquisitionStatus && !wasAcquiring && commutatorAutoConnect && !commutatorConnected)
                {
                    if (validPort)
                        commutatorConnected = true;

                    else
                    {
                        log.Warning($"{nameof(hardwareSettings.Commutator.AutoConnect)} is enabled but no commutator was connected; the selected COM port '{portName}' is not valid.");
                    }
                }
                wasAcquiring = AcquisitionStatus;

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

                        ImGui.SameLine();
                        ImGui.Text("Control Panel");

                        ImGui.Spacing();
                        ImGui.Separator();
                        ImGui.Spacing();

                        float configButtonWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
                        if (ImGui.Button("Export Config##miniscope_save", new Vector2(configButtonWidth, 0)))
                        {
                            requestType = ConfigurationRequestType.ManualSave;
                        }
                        if (ImGui.BeginItemTooltip())
                        {
                            ImGui.Text("Save the current configuration.");
                            ImGui.EndTooltip();
                        }

                        ImGui.SameLine();
                        if (ImGui.Button("Import Config##miniscope_load", new Vector2(configButtonWidth, 0)))
                        {
                            requestType = ConfigurationRequestType.ManualLoad;
                        }
                        if (ImGui.BeginItemTooltip())
                        {
                            ImGui.Text("Load an existing configuration.");
                            ImGui.EndTooltip();
                        }

                        ImGui.Spacing();
                        ImGui.Separator();
                        ImGui.Spacing();

                        float fileReserve = layout.RecordingSectionHeight > 0f ? layout.RecordingSectionHeight + ImGui.GetStyle().ItemSpacing.Y : 0f;
                        ImGui.BeginChild("##settings_content", new Vector2(-1f, -fileReserve), ImGuiChildFlags.None);

                        ImGui.SetNextItemOpen(true, ImGuiCond.Once);
                        if (ImGui.CollapsingHeader("Miniscope##miniscope_header"))
                        {
                            ImGui.BeginChild("##miniscope_group", new Vector2(0f, 0f), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY);

                            ImGui.AlignTextToFramePadding();
                            ImGui.Text("Focus: ");
                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(-1f);
                            double focusMin = -100, focusMax = 100;
                            ImGui.SliderScalar("##focus", ImGuiDataType.Double, &focus, &focusMin, &focusMax, "%.1f", ImGuiSliderFlags.AlwaysClamp);

                            ImGui.AlignTextToFramePadding();
                            ImGui.Text("LED Brightness: ");
                            if (ImGui.BeginItemTooltip())
                            {
                                ImGui.Text("Change the brightness of the LED.\n\nTo type in a specific value, press `Ctrl` and click on the slider before typing. Press enter to set the brightness value.");
                                ImGui.EndTooltip();
                            }
                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(-1f);
                            double brightnessMin = 0, brightnessMax = 100;
                            ImGui.SliderScalar("##ledbrightness", ImGuiDataType.Double, &ledBrightness, &brightnessMin, &brightnessMax, "%.1f", ImGuiSliderFlags.AlwaysClamp);

                            if (ImGui.BeginTable("##row2", 2, ImGuiTableFlags.SizingStretchSame))
                            {
                                ImGui.TableNextColumn();
                                ImGui.AlignTextToFramePadding();
                                ImGui.Text("Frame Rate: ");
                                ImGui.SameLine();
                                ImGui.SetNextItemWidth(-1f);
                                int frameRateIndex = Array.IndexOf(FrameRateValues, frameRate.ToString());
                                if (ImGui.Combo("##framerate", ref frameRateIndex, FrameRateValues, FrameRateValues.Length))
                                {
                                    if (Enum.TryParse<FrameRateV4>(FrameRateValues[frameRateIndex], out var result))
                                        frameRate = result;
                                }

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

                                ImGui.TableNextColumn();
                                ImGui.AlignTextToFramePadding();
                                ImGui.Text("LED Trigger: ");
                                ImGui.SameLine();
                                ImGui.SetNextItemWidth(-1f);
                                int digitalInIndex = Array.IndexOf(DigitalInValues, ledRespectsDigitalIn);
                                if (ImGui.Combo("##ledrespectdigitalin", ref digitalInIndex, DigitalInNames, DigitalInNames.Length))
                                {
                                    ledRespectsDigitalIn = DigitalInValues[digitalInIndex];
                                }

                                ImGui.EndTable();
                            }

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

                        if (ImGui.CollapsingHeader("Commutator##commutator_header"))
                        {
                            ImGui.BeginChild("##commutator_group", new Vector2(0f, 0f), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY);

                            ImGui.AlignTextToFramePadding();
                            ImGui.Text("COM Port: ");
                            ImGui.SameLine();

                            var style = ImGui.GetStyle();
                            float refreshButtonWidth = ImGui.CalcTextSize("Refresh").X + style.FramePadding.X * 2;
                            float comboWidth = ImGui.GetContentRegionAvail().X - refreshButtonWidth - style.ItemSpacing.X;
                            ImGui.SetNextItemWidth(comboWidth);

                            if (commutatorConnected)
                                ImGui.BeginDisabled();

                            int portIndex = Array.IndexOf(portNames, portName);
                            if (portIndex < 0)
                            {
                                if (portNames.Length > 0)
                                {
                                    portIndex = 0;
                                    portName = portNames[0];
                                }
                                else if (!string.IsNullOrEmpty(portName))
                                {
                                    portName = "";
                                }
                            }

                            if (ImGui.BeginCombo("##comport", portIndex >= 0 ? portNames[portIndex] : "No commutator found"))
                            {
                                for (int i = 0; i < portNames.Length; i++)
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

                            ImGui.SameLine();
                            if (ImGui.Button("Refresh##comrefresh"))
                            {
                                portNames = SerialPort.GetPortNames();
                            }

                            if (commutatorConnected)
                                ImGui.EndDisabled();

                            if (ImGui.BeginTable("##commutator_connect", 2, ImGuiTableFlags.SizingStretchSame))
                            {
                                ImGui.TableNextColumn();
                                ImGui.Checkbox("Auto Connect##commutator_autoconnect", ref commutatorAutoConnect);
                                if (ImGui.BeginItemTooltip())
                                {
                                    ImGui.Text("Automatically connect the commutator when acquisition starts, provided a valid COM port is selected.");
                                    ImGui.EndTooltip();
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

                                ImGui.EndTable();
                            }

                            ImGui.Separator();

                            if (!commutatorConnected)
                                ImGui.BeginDisabled();

                            if (ImGui.BeginTable("##commutator_checkboxes", 2, ImGuiTableFlags.SizingStretchSame))
                            {
                                ImGui.TableNextColumn();
                                ImGui.Checkbox("Enable##commutator_enable", ref commutatorEnable);

                                ImGui.TableNextColumn();
                                ImGui.Checkbox("Enable LED##commutator_led", ref commutatorEnableLed);

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
                    ConfigFilePath = ""
                };

                observer.OnNext(Tuple.Create(layout, updatedHardwareSettings, updatedConfigurationRequest));
            },
            observer.OnError,
            observer.OnCompleted);

            return new CompositeDisposable(logSubscription, source.SubscribeSafe(sourceObserver));
        });
    }
}
