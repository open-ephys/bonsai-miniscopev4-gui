using Bonsai;
using Bonsai.IO;
using Hexa.NET.ImGui;
using OpenEphys.Miniscope;
using System;
using System.ComponentModel;
using System.IO;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Renders the "Recording" controls (file saving and recording), anchored to the bottom of the settings sidebar.
/// </summary>
/// <remarks>
/// Renders into the shared sidebar child window opened (but not closed) by <see cref="SettingsPanel"/>,
/// and closes it once its own content is done, so the two panels form a single visual region. Content
/// is skipped (but the child is still closed) while <see cref="SettingsPanel.SidebarOpen"/> is false, so
/// collapsing the sidebar hides this section along with the rest of the settings. Its own content renders
/// into an auto-sized child so <see cref="LastHeight"/> can be measured; <see cref="SettingsPanel"/> uses
/// that (one frame stale, since the height is otherwise unknown until it renders) to bound its own
/// collapsible content and keep this section anchored to a fixed distance from the bottom.
/// </remarks>
[Combinator]
[Description("Renders the recording and file saving controls.")]
public class FilePanel
{
    /// <summary>
    /// Gets or sets the acquisition status of the GUI.
    /// </summary>
    public bool AcquisitionStatus { get; set; }

    /// <summary>
    /// Gets the height, in pixels, that the File section's content occupied last frame.
    /// </summary>
    internal static float LastHeight { get; private set; }

    static readonly string[] DigitalInNames = Enum.GetNames(typeof(MiniscopeDaqDigitalIn));
    static readonly MiniscopeDaqDigitalIn[] DigitalInValues = (MiniscopeDaqDigitalIn[])Enum.GetValues(typeof(MiniscopeDaqDigitalIn));
    static readonly string[] PathSuffixValues = Enum.GetNames(typeof(PathSuffix));

    /// <summary>
    /// Renders the file saving and recording controls and returns an updated <see cref="FileSettings"/> alongside each source value.
    /// </summary>
    /// <param name="source">A sequence of values tied to the render tick of DearImGui.</param>
    /// <returns>A sequence of values paired with the file settings updated from the rendered controls.</returns>
    public unsafe IObservable<Tuple<TSource, FileSettings>> Process<TSource>(
        IObservable<Tuple<TSource, FileSettings>> source)
    {
        return Observable.Create<Tuple<TSource, FileSettings>>(observer =>
        {
            const nuint bufSize = 1024;
            string fileName = string.Empty;
            Task<string> saveDialogTask = null;
            bool isModalOpen = false;

            bool recordRequested = false;
            bool lastRecordButtonInput = false;
            bool recordStateInitialized = false;

            var sourceObserver = Observer.Create<Tuple<TSource, FileSettings>>(value =>
            {
                var fileSettings = value.Item2;
                var triggerMode = fileSettings.TriggerMode;

                if (!recordStateInitialized)
                {
                    recordRequested = fileSettings.RecordButton;
                    lastRecordButtonInput = fileSettings.RecordButton;
                    recordStateInitialized = true;
                }
                if (fileSettings.RecordButton != lastRecordButtonInput)
                {
                    recordRequested = fileSettings.RecordButton;
                    lastRecordButtonInput = fileSettings.RecordButton;
                }

                bool recordButton = recordRequested;
                fileName = fileSettings.FileName;
                PathSuffix suffix = fileSettings.Suffix;
                int recordingDurationSeconds = fileSettings.RecordingDuration;
                bool useRecordDuration = fileSettings.UseRecordDuration;
                bool isCompressed = fileSettings.CompressVideo;
                bool automaticRestart = fileSettings.AutomaticRestart;
                var triggerInput = fileSettings.TriggerInput;
                int triggerIndex = Array.IndexOf(DigitalInValues, triggerInput);

                if (SettingsPanel.SidebarOpen)
                {
                    ImGui.BeginChild("##file_pane", new Vector2(-1f, 0f), ImGuiChildFlags.AutoResizeY);

                    ImGui.Text("Recording");
                    ImGui.Separator();

                    ImGui.Text("Base File Name");
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
                    LastHeight = ImGui.GetItemRectSize().Y;
                }
                else
                {
                    LastHeight = 0f;
                }

                ImGui.EndChild(); // closes the shared sidebar child opened by SettingsPanel

                recordRequested = recordButton;

                var updatedFileSettings = new FileSettings(recordButton, triggerMode, isCompressed, fileName, suffix, recordingDurationSeconds, useRecordDuration, triggerInput, automaticRestart);

                observer.OnNext(Tuple.Create(value.Item1, updatedFileSettings));
            },
            observer.OnError,
            observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }

    static string GetDirectory(string path) => Path.GetDirectoryName(Path.GetFullPath(string.IsNullOrEmpty(path) ? "./" : path));
}
