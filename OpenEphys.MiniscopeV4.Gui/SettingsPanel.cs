using Bonsai;
using Bonsai.IO;
using Hexa.NET.ImGui;
using OpenEphys.Miniscope;
using System;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Renders all settings panels in a collapsible sidebar and returns an updated <see cref="HardwareSettings"/>.
/// </summary>
[Combinator]
[Description("Renders all settings panels in a collapsible right-hand sidebar.")]
public class SettingsPanel
{
    /// <summary>
    /// Gets or sets the acquisition status of the GUI.
    /// </summary>
    public bool AcquisitionStatus { get; set; }

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
    static readonly string[] PathSuffixValues = Enum.GetNames(typeof(PathSuffix));

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
            const nuint bufSize = 1024;
            string fileName = string.Empty;
            Task<string> saveDialogTask = null;
            bool isModalOpen = false;

            bool recordRequested = false;
            bool lastRecordButtonInput = false;
            bool recordStateInitialized = false;

            var portNames = SerialPort.GetPortNames();

            var sourceObserver = Observer.Create<Tuple<TSource, HardwareSettings>>(value =>
            {
                var dto = value.Item2;

                double ledBrightness = dto.Miniscope.LedBrightness;
                double focus = dto.Miniscope.Focus;
                GainV4 sensorGain = dto.Miniscope.SensorGain;
                FrameRateV4 frameRate = dto.Miniscope.FrameRate;
                MiniscopeDaqDigitalIn ledRespectsDigitalIn = dto.Miniscope.LedRespectsDigitalIn;
                var triggerMode = dto.File.TriggerMode;

                if (!recordStateInitialized)
                {
                    recordRequested = dto.File.RecordButton;
                    lastRecordButtonInput = dto.File.RecordButton;
                    recordStateInitialized = true;
                }
                if (dto.File.RecordButton != lastRecordButtonInput)
                {
                    recordRequested = dto.File.RecordButton;
                    lastRecordButtonInput = dto.File.RecordButton;
                }

                bool recordButton = recordRequested;
                fileName = dto.File.FileName;
                PathSuffix suffix = dto.File.Suffix;
                int recordingDurationSeconds = dto.File.RecordingDuration;
                bool useRecordDuration = dto.File.UseRecordDuration;
                bool isCompressed = dto.File.CompressVideo;
                bool automaticRestart = dto.File.AutomaticRestart;
                var triggerInput = dto.File.TriggerInput;
                int triggerIndex = Array.IndexOf(DigitalInValues, triggerInput);

                string portName = dto.Commutator.PortName;
                bool commutatorConnected = dto.Commutator.IsConnected;
                bool commutatorEnable = dto.Commutator.Enable;
                bool commutatorEnableLed = dto.Commutator.EnableLed;

                float availableX = ImGui.GetContentRegionAvail().X;
                float panelWidth = GetCurrentWidth(availableX);

                float consoleReserve = ConsoleLayout.ReservedHeight(ImGui.GetStyle().ItemSpacing.Y);
                ImGui.BeginChild("##settings_pane", new Vector2(panelWidth - ImGui.GetStyle().ChildBorderSize, -consoleReserve), ImGuiChildFlags.Borders);

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
                    ImGui.Text("Settings");
                    ImGui.Separator();

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

                        if (commutatorConnected)
                        {
                            using (Palette.PushColor(ImGuiCol.Text, Palette.GreenHovered))
                                ImGui.Text("Status: Connected");
                        }
                        else
                        {
                            ImGui.TextDisabled("Status: Disconnected");
                        }

                        ImGui.EndChild();
                    }
                }

                ImGui.SetNextItemOpen(true, ImGuiCond.Once);
                if (ImGui.CollapsingHeader("File##file_header"))
                { 
                    ImGui.BeginChild("##file_group", new Vector2(0f, 0f), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY);

                    ImGui.Text("File Name Template");
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("Choose the location and format to save all files.");
                        ImGui.Text("Video files will have '.avi' added the file format, and CSV files will have '.csv' added.");
                        ImGui.Text("If Suffix is set, the selected suffix will be added after the format and before the extension.");
                        ImGui.EndTooltip();
                    }

                    const string browseLabel = "...";
                    const string openLabel = "Browse";
                    float browseWidth = ImGui.CalcTextSize(browseLabel).X + ImGui.GetStyle().FramePadding.X * 2f;
                    float openWidth = ImGui.CalcTextSize(openLabel).X + ImGui.GetStyle().FramePadding.X * 2f;
                    float inputWidth = ImGui.GetContentRegionAvail().X - browseWidth - openWidth - ImGui.GetStyle().ItemSpacing.X * 2f;

                    ImGui.SetNextItemWidth(inputWidth);
                    ImGui.InputText("##filename", ref fileName, bufSize, ImGuiInputTextFlags.ElideLeft);
                    ImGui.SameLine();
                    if (ImGui.Button($"{browseLabel}##choose_filename_button", new Vector2(browseWidth, 0)))
                    {
                        if (saveDialogTask == null || saveDialogTask.IsCompleted)
                        {
                            saveDialogTask = Task.Run(() =>
                            {
                                string result = string.Empty;
                                Thread t = new(() =>
                                {
                                    SaveFileDialog dlg = new()
                                    {
                                        InitialDirectory = GetDirectory(fileName),
                                        Filter = "All Files|*.*",
                                        Title = "Choose where to save Miniscope data.",
                                        AddExtension = false,
                                        CheckFileExists = false,
                                        CheckPathExists = false,
                                        FileName = Path.GetFileName(fileName)
                                    };
                                    if (dlg.ShowDialog() == DialogResult.OK)
                                        result = dlg.FileName;
                                });
                                t.SetApartmentState(ApartmentState.STA);
                                t.Start();
                                t.Join();
                                return result;
                            });
                        }
                    }

                    string noFolderFoundPopupName = "No folder found##no_folder_found";

                    ImGui.SameLine();
                    if (ImGui.Button($"{openLabel}##open_folder_button", new Vector2(openWidth, 0)))
                    {
                        var dir = GetDirectory(fileName);
                        if (Directory.Exists(dir))
                            System.Diagnostics.Process.Start("explorer.exe", dir);

                        else
                        {
                            ImGui.OpenPopup(noFolderFoundPopupName);
                            isModalOpen = true;
                        }
                    }

                    if (ImGui.BeginPopupModal(noFolderFoundPopupName, ref isModalOpen, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
                    {
                        ImGui.Text($"Could not find the folder '{GetDirectory(fileName)}'.");

                        if (ImGui.Button("Okay##close_modal_window"))
                        {
                            ImGui.CloseCurrentPopup();
                            isModalOpen = false;
                        }

                        ImGui.EndPopup();
                    }

                    if (saveDialogTask != null && saveDialogTask.IsCompleted)
                    {
                        var result = saveDialogTask.Result;
                        if (!string.IsNullOrEmpty(result))
                            fileName = result;
                        saveDialogTask = null;
                    }

                    if (ImGui.BeginTable("##writer_parameters", 2, ImGuiTableFlags.SizingStretchSame))
                    {
                        ImGui.TableNextColumn();
                        ImGui.AlignTextToFramePadding();
                        ImGui.Text("Suffix:");
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(-1f);

                        int currentPathSuffix = (int)suffix;
                        if (ImGui.Combo("##path_suffix", ref currentPathSuffix, PathSuffixValues, PathSuffixValues.Length))
                            suffix = (PathSuffix)currentPathSuffix;

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(-1f);
                        ImGui.Checkbox("Compress Video##compress_video", ref isCompressed);

                        ImGui.EndTable();
                    }

                    ImGui.Separator();

                    if (recordButton) ImGui.BeginDisabled();
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Mode: ");
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Manual##record_mode_manual", !triggerMode) && triggerMode)
                    {
                        triggerMode = false;
                    }
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Triggered##record_mode_triggered", triggerMode) && !triggerMode)
                    {
                        triggerMode = true;
                    }
                    if (recordButton) ImGui.EndDisabled();

                    var recordingSettingsHeight = ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetStyle().ItemSpacing.Y * 2;

                    if (ImGui.BeginChild("##recording_settings", new Vector2(-1, recordingSettingsHeight), ImGuiChildFlags.None))
                    {
                        if (!triggerMode)
                        {
                            if (recordButton) ImGui.BeginDisabled();
                            ImGui.Checkbox("Use Recording Duration##use_record_duration", ref useRecordDuration);

                            if (!useRecordDuration) ImGui.BeginDisabled();

                            if (ImGui.BeginTable("##record_duration_table", 2, ImGuiTableFlags.SizingStretchSame))
                            {
                                ImGui.TableNextColumn();

                                ImGui.AlignTextToFramePadding();
                                ImGui.Text("Duration [s]:");
                                ImGui.SameLine();
                                ImGui.SetNextItemWidth(-1f);
                                if (ImGui.InputInt("##recording_duration", ref recordingDurationSeconds, 0, 0, ImGuiInputTextFlags.AutoSelectAll))
                                {
                                    recordingDurationSeconds = Math.Max(1, recordingDurationSeconds);
                                }

                                ImGui.TableNextColumn();
                                ImGui.Checkbox("Auto Restart##automatic_restart", ref automaticRestart);
                                if (ImGui.BeginItemTooltip())
                                {
                                    ImGui.Text("When enabled, a new recording starts automatically each time the\nrecording duration elapses, until you press Stop Recording.");
                                    ImGui.EndTooltip();
                                }

                                ImGui.EndTable();
                            }

                            if (!useRecordDuration) ImGui.EndDisabled();
                            if (recordButton) ImGui.EndDisabled();
                        }
                        else
                        {
                            ImGui.AlignTextToFramePadding();
                            ImGui.Text("Digital Input: ");
                            ImGui.SameLine();
                            ImGui.SetNextItemWidth(-1f);
                            if (recordButton) ImGui.BeginDisabled();
                            if (ImGui.BeginCombo("##trigger_input", DigitalInNames[triggerIndex]))
                            {
                                foreach (var val in DigitalInValues)
                                {
                                    if (val == MiniscopeDaqDigitalIn.None) continue;

                                    bool selected = triggerInput == val;
                                    if (ImGui.Selectable(val.ToString(), selected))
                                        triggerInput = val;

                                    if (selected)
                                        ImGui.SetItemDefaultFocus();
                                }
                                ImGui.EndCombo();
                            }
                            if (recordButton) ImGui.EndDisabled();
                        }
                    }

                    ImGui.EndChild();

                    using (Palette.PushButtonColors(
                        recordButton ? Palette.Red : Palette.Green,
                        recordButton ? Palette.RedHovered : Palette.GreenHovered,
                        recordButton ? Palette.RedActive : Palette.GreenActive))
                    {
                        Vector2 recordButtonSize = new(-1f, ImGui.GetFrameHeight() * 2);
                        if (!AcquisitionStatus) ImGui.BeginDisabled();
                        string recordLabel = !triggerMode
                            ? (recordButton ? "Stop Recording##record_button" : "Record##record_button")
                            : (recordButton ? "Disarm##record_button" : "Arm Recording##record_button");
                        if (ImGui.Button(recordLabel, recordButtonSize))
                        {
                            recordButton = !recordButton;
                        }
                        if (!AcquisitionStatus) ImGui.EndDisabled();
                    }

                    ImGui.EndChild();
                }

                ImGui.EndChild();

                recordRequested = recordButton;

                var updatedHardwareSettings = new HardwareSettings(
                    new MiniscopeSettings(ledBrightness, focus, sensorGain, frameRate, ledRespectsDigitalIn),
                    new FileSettings(recordButton, triggerMode, isCompressed, fileName, suffix, recordingDurationSeconds, useRecordDuration, triggerInput, automaticRestart),
                    new CommutatorSettings(portName, commutatorConnected, commutatorEnable, commutatorEnableLed));

                observer.OnNext(Tuple.Create(value.Item1, updatedHardwareSettings));
            },
            observer.OnError,
            observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }

    static string GetDirectory(string path) => Path.GetDirectoryName(Path.GetFullPath(string.IsNullOrEmpty(path) ? "./" : path));
}
