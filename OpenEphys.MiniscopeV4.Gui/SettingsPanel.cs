using Bonsai;
using Hexa.NET.ImGui;
using OpenEphys.Miniscope;
using System;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Renders all settings panels in a collapsible sidebar and returns an updated <see cref="HardwareSettings"/>.
/// </summary>
/// <remarks>
/// Opens the shared sidebar child window but does not close it, so that <see cref="FilePanel"/> can render
/// into the same region immediately afterward and close it.
/// </remarks>
[Combinator]
[Description("Renders all settings panels in a collapsible right-hand sidebar.")]
public class SettingsPanel
{
    /// <summary>
    /// Gets or sets the acquisition status of the GUI.
    /// </summary>
    public bool AcquisitionStatus { get; set; }

    /// <summary>
    /// Gets whether the sidebar is currently expanded, so <see cref="FilePanel"/> knows whether to
    /// render its content into the shared child window or leave it collapsed to icon width.
    /// </summary>
    internal static bool SidebarOpen { get; private set; }

    static float ExpandedWidth => 375f * UiScale.Current;
    static float CollapsedWidth => 36f * UiScale.Current;

    bool settingsOpen = true;

    float GetCurrentWidth(float availableX)
    {
        if (settingsOpen)
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
    /// Renders the settings sidebar and returns an updated <see cref="HardwareSettings"/> alongside each source value.
    /// </summary>
    /// <param name="source">A sequence of values tied to the render tick of DearImGui.</param>
    /// <returns>A sequence of values paired with the settings updated from the rendered controls.</returns>
    public unsafe IObservable<Tuple<TSource, HardwareSettings>> Process<TSource>(
        IObservable<Tuple<TSource, HardwareSettings>> source)
    {
        return Observable.Create<Tuple<TSource, HardwareSettings>>(observer =>
        {
            var portNames = SerialPort.GetPortNames();

            var sourceObserver = Observer.Create<Tuple<TSource, HardwareSettings>>(value =>
            {
                var dto = value.Item2;

                double ledBrightness = dto.Miniscope.LedBrightness;
                double focus = dto.Miniscope.Focus;
                GainV4 sensorGain = dto.Miniscope.SensorGain;
                FrameRateV4 frameRate = dto.Miniscope.FrameRate;
                MiniscopeDaqDigitalIn ledRespectsDigitalIn = dto.Miniscope.LedRespectsDigitalIn;

                string portName = dto.Commutator.PortName;
                bool commutatorConnected = dto.Commutator.IsConnected;
                bool commutatorEnable = dto.Commutator.Enable;
                bool commutatorEnableLed = dto.Commutator.EnableLed;

                float availableX = ImGui.GetContentRegionAvail().X;
                float panelWidth = GetCurrentWidth(availableX);

                float consoleReserve = ConsoleLayout.ReservedHeight(ImGui.GetStyle().ItemSpacing.Y);
                ImGui.BeginChild("##settings_pane", new Vector2(panelWidth - ImGui.GetStyle().ChildBorderSize, -consoleReserve), ImGuiChildFlags.Borders);

                SidebarOpen = settingsOpen;

                if (!settingsOpen)
                {
                    if (ImGui.ArrowButton("##settings_open", ImGuiDir.Right))
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
                    ImGui.Separator();

                    float fileReserve = FilePanel.LastHeight > 0f ? FilePanel.LastHeight + ImGui.GetStyle().ItemSpacing.Y : 0f;
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
                        float connectButtonWidth = ImGui.CalcTextSize("Disconnect").X + style.FramePadding.X * 2;
                        float comboWidth = ImGui.GetContentRegionAvail().X - refreshButtonWidth - connectButtonWidth - style.ItemSpacing.X * 2;
                        ImGui.SetNextItemWidth(comboWidth);

                        if (commutatorConnected)
                            ImGui.BeginDisabled();

                        int portIndex = Array.IndexOf(portNames, portName);
                        if (portIndex < 0 && portNames.Length > 0)
                        {
                            portIndex = 0;
                            portName = portNames[0];
                        }
                        if (ImGui.Combo("##comport", ref portIndex, portNames, portNames.Length) && portNames.Length > 0)
                        {
                            portName = portNames[portIndex];
                        }

                        ImGui.SameLine();
                        if (ImGui.Button("Refresh##comrefresh"))
                        {
                            portNames = SerialPort.GetPortNames();
                        }

                        if (commutatorConnected)
                            ImGui.EndDisabled();

                        ImGui.SameLine();
                        if (ImGui.Button(commutatorConnected ? "Disconnect##combutton" : "Connect##combutton"))
                        {
                            commutatorConnected = !commutatorConnected;
                        }

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

                        var statusColor = commutatorConnected
                                ? new Vector4(0.2f, 0.8f, 0.2f, 1f)
                                : new Vector4(0.6f, 0.6f, 0.6f, 1f);
                        ImGui.PushStyleColor(ImGuiCol.Text, statusColor);
                        var displayStatus = commutatorConnected ? "Status: Connected" : "Status: Disconnected";
                        ImGui.Text(displayStatus);
                        ImGui.PopStyleColor();

                        ImGui.EndChild();
                    }

                    ImGui.EndChild();
                }

                var updatedHardwareSettings = new HardwareSettings(
                    new MiniscopeSettings(ledBrightness, focus, sensorGain, frameRate, ledRespectsDigitalIn),
                    new CommutatorSettings(portName, commutatorConnected, commutatorEnable, commutatorEnableLed));

                observer.OnNext(Tuple.Create(value.Item1, updatedHardwareSettings));
            },
            observer.OnError,
            observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }
}
