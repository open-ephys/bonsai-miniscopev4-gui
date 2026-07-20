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

    string configFilePath = string.Empty;

    string ConfigFilePath
    {
        get => string.IsNullOrEmpty(configFilePath) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : configFilePath;
        set => configFilePath = value;
    }

    /// <summary>
    /// Renders the settings sidebar and returns an updated <see cref="HardwareSettings"/> alongside the shared layout.
    /// </summary>
    /// <param name="source">A sequence pairing the shared <see cref="GuiLayout"/> with the current <see cref="HardwareSettings"/>, tied to the render tick of DearImGui.</param>
    /// <param name="logSource">The shared <see cref="MiniscopeLog"/> instance (typically a <c>BehaviorSubject</c>), captured once and passed to <see cref="SettingsFile"/> when saving or loading configs.</param>
    /// <returns>A sequence pairing the updated <see cref="GuiLayout"/> with the settings updated from the rendered controls.</returns>
    public unsafe IObservable<Tuple<GuiLayout, HardwareSettings>> Process(IObservable<Tuple<GuiLayout, HardwareSettings>> source, IObservable<MiniscopeLog> logSource)
    {
        return Observable.Create<Tuple<GuiLayout, HardwareSettings>>(observer =>
        {
            var portNames = SerialPort.GetPortNames();
            Task<string> saveConfigTask = null;
            Task<string> loadConfigTask = null;

            // NB: Expect this to be a BehaviorSubject, so we can take the first value immediately.
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
                        ImGui.Separator();

                        float fileReserve = layout.RecordingSectionHeight > 0f ? layout.RecordingSectionHeight + ImGui.GetStyle().ItemSpacing.Y : 0f;
                        ImGui.BeginChild("##settings_content", new Vector2(-1f, -fileReserve), ImGuiChildFlags.None);

                        ImGui.SetNextItemOpen(true, ImGuiCond.Once);
                        if (ImGui.CollapsingHeader("Miniscope##miniscope_header"))
                        {
                            ImGui.BeginChild("##miniscope_group", new Vector2(0f, 0f), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY);

                            float configButtonWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
                            if (ImGui.Button("Save Config##miniscope_save", new Vector2(configButtonWidth, 0)))
                            {
                                if (saveConfigTask == null || saveConfigTask.IsCompleted)
                                    saveConfigTask = CreateSaveConfigDialogTask();
                            }
                            if (ImGui.BeginItemTooltip())
                            {
                                ImGui.Text("Save the current Miniscope settings to a JSON file.");
                                ImGui.EndTooltip();
                            }

                            ImGui.SameLine();
                            if (ImGui.Button("Load Config##miniscope_load", new Vector2(configButtonWidth, 0)))
                            {
                                if (loadConfigTask == null || loadConfigTask.IsCompleted)
                                    loadConfigTask = CreateOpenConfigDialogTask();
                            }
                            if (ImGui.BeginItemTooltip())
                            {
                                ImGui.Text("Load Miniscope settings from a JSON file.");
                                ImGui.EndTooltip();
                            }

                            if (saveConfigTask != null && saveConfigTask.IsCompleted)
                            {
                                var savePath = saveConfigTask.Result;
                                saveConfigTask = null;
                                if (!string.IsNullOrEmpty(savePath))
                                {
                                    SettingsFile.Save(savePath, hardwareSettings.Miniscope, log);
                                    ConfigFilePath = savePath;
                                }
                            }

                            if (loadConfigTask != null && loadConfigTask.IsCompleted)
                            {
                                var loadPath = loadConfigTask.Result;
                                loadConfigTask = null;
                                if (!string.IsNullOrEmpty(loadPath) && SettingsFile.TryLoad(loadPath, hardwareSettings.Miniscope, log, out var loaded))
                                {
                                    ledBrightness = loaded.LedBrightness;
                                    focus = loaded.Focus;
                                    sensorGain = loaded.SensorGain;
                                    frameRate = loaded.FrameRate;
                                    ledRespectsDigitalIn = loaded.LedRespectsDigitalIn;

                                    ConfigFilePath = loadPath;
                                }
                            }

                            ImGui.Spacing();
                            ImGui.Separator();
                            ImGui.Spacing();

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

                var updatedHardwareSettings = new HardwareSettings(
                    new MiniscopeSettings(ledBrightness, focus, sensorGain, frameRate, ledRespectsDigitalIn),
                    new CommutatorSettings(portName, commutatorConnected, commutatorEnable, commutatorEnableLed));

                observer.OnNext(Tuple.Create(layout, updatedHardwareSettings));
            },
            observer.OnError,
            observer.OnCompleted);

            return new CompositeDisposable(logSubscription, source.SubscribeSafe(sourceObserver));
        });
    }

    Task<string> CreateSaveConfigDialogTask() => FileDialogHelpers.RunDialogTask(() => new SaveFileDialog
    {
        InitialDirectory = ConfigFilePath,
        Filter = "JSON files (*.json)|*.json|All Files|*.*",
        Title = "Choose where to save the Miniscope configuration.",
        AddExtension = true,
        DefaultExt = "json",
        FileName = "config.json",
        OverwritePrompt = true,
    }, (dlg) => (dlg as SaveFileDialog).FileName);

    Task<string> CreateOpenConfigDialogTask() => FileDialogHelpers.RunDialogTask(() => new OpenFileDialog
    {
        InitialDirectory = ConfigFilePath,
        Filter = "JSON files (*.json)|*.json|All Files|*.*",
        Title = "Choose a Miniscope configuration to load.",
        CheckFileExists = true,
        Multiselect = false
    }, (dlg) => (dlg as OpenFileDialog).FileName);
}
