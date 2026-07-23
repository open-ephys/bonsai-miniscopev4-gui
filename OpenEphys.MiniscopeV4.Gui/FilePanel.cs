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
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Renders the "Recording" controls (file saving and recording), anchored to the bottom of the settings sidebar.
/// </summary>
/// <remarks>
/// Renders into the shared sidebar child window opened (but not closed) by <see cref="SettingsPanel"/>,
/// and closes it once its own content is done, so the two panels form a single visual region. Content
/// is skipped (but the child is still closed) while <see cref="GuiLayout.SidebarOpen"/> is false, so
/// collapsing the sidebar hides this section along with the rest of the settings. Its own content renders
/// into an auto-sized child so <see cref="GuiLayout.RecordingSectionHeight"/> can be measured;
/// <see cref="SettingsPanel"/> uses that (one frame stale, since the height is otherwise unknown until it
/// renders) to bound its own collapsible content and keep this section anchored to a fixed distance from
/// the bottom. The two panels coordinate through the threaded <see cref="GuiLayout"/>.
/// </remarks>
[Combinator]
[Description("Renders the recording and file saving controls.")]
public class FilePanel
{
    /// <summary>
    /// Gets or sets the acquisition status of the GUI.
    /// </summary>
    public bool AcquisitionStatus { get; set; }

    static readonly string[] DigitalInNames = Enum.GetNames(typeof(MiniscopeDaqDigitalIn));
    static readonly MiniscopeDaqDigitalIn[] DigitalInValues = (MiniscopeDaqDigitalIn[])Enum.GetValues(typeof(MiniscopeDaqDigitalIn));
    static readonly string[] PathSuffixValues = Enum.GetNames(typeof(PathSuffix));

    /// <summary>
    /// Renders the file saving and recording controls and returns an updated <see cref="FileSettings"/> alongside the shared layout.
    /// </summary>
    /// <param name="source">A sequence pairing the shared <see cref="GuiLayout"/> with the current <see cref="FileSettings"/>, tied to the render tick of DearImGui.</param>
    /// <returns>A sequence pairing the updated <see cref="GuiLayout"/> with the file settings updated from the rendered controls.</returns>
    public IObservable<Tuple<GuiLayout, FileSettings>> Process(IObservable<Tuple<GuiLayout, FileSettings>> source)
    {
        return Observable.Create<Tuple<GuiLayout, FileSettings>>(observer =>
        {
            const nuint bufSize = 1024;
            string fileName = string.Empty;
            Task<string> saveDialogTask = null;
            bool shouldStartRecordingWhenCompleted = false;

            bool recordRequested = false;
            bool lastRecordButtonInput = false;
            bool recordStateInitialized = false;
            DateTime? recordingStart = null;

            var sourceObserver = Observer.Create<Tuple<GuiLayout, FileSettings>>(value =>
            {
                var layout = value.Item1;
                var fileSettings = value.Item2;
                var recordingMode = fileSettings.RecordingMode;

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
                int totalDurationSeconds = fileSettings.TotalDuration;
                bool useTotalDuration = fileSettings.UseTotalDuration;
                bool isCompressed = fileSettings.CompressVideo;
                bool automaticRestart = fileSettings.AutomaticRestart;
                var triggerInput = fileSettings.TriggerInput;
                int triggerIndex = Array.IndexOf(DigitalInValues, triggerInput);

                if (triggerIndex < 1) triggerIndex = 1;

                if (recordButton)
                {
                    recordingStart ??= DateTime.Now;
                }
                else if (recordingStart != null)
                {
                    recordingStart = null;
                }

                if (layout.ImageExpanded)
                {
                    // SettingsPanel didn't open a shared child this frame (fully hidden), so there's
                    // nothing here to render into or close.
                    layout = layout with { RecordingSectionHeight = 0f };
                }
                else if (layout.SidebarOpen)
                {
                    ImGui.BeginChild("##file_pane", new Vector2(-1f, 0f), ImGuiChildFlags.AutoResizeY);

                    ImGui.Separator();
                    ImGui.Text("Recording");
                    ImGui.Dummy(new Vector2(0f, ImGui.GetStyle().ItemSpacing.Y));

                    ImGui.Text("Data Path");
                    if (ImGui.BeginItemTooltip())
                    {
                        ImGui.Text("Choose the location and format to save all files.");
                        ImGui.Text("If Suffix is set, the selected suffix will be added after the format and before the extension.");
                        ImGui.Text("Video files will have '.avi' appended, CSV files will have '.csv' appended, and log files will have '.log' appended.");
                        ImGui.EndTooltip();
                    }

                    const string selectLabel = "...";
                    const string browseLabel = "Browse";
                    var (selectWidth, browseWidth, inputWidth) = CalculateFileNameInputWidth(selectLabel, browseLabel);

                    ImGui.SetNextItemWidth(inputWidth);
                    ImGui.InputText("##filename", ref fileName, bufSize, ImGuiInputTextFlags.ElideLeft);
                    ImGui.SameLine();
                    if (ImGui.Button($"{selectLabel}##choose_filename_button", new Vector2(selectWidth, 0)))
                    {
                        if (saveDialogTask == null || saveDialogTask.IsCompleted)
                        {
                            saveDialogTask = CreateSaveFileDialogTask(fileName);
                        }
                    }

                    if (saveDialogTask != null && saveDialogTask.IsCompleted)
                    {
                        var result = saveDialogTask.Result;
                        if (!string.IsNullOrEmpty(result))
                            fileName = result;
                        saveDialogTask = null;

                        if (shouldStartRecordingWhenCompleted && !string.IsNullOrEmpty(fileName))
                        {
                            recordButton = true;
                            shouldStartRecordingWhenCompleted = false;
                        }
                    }

                    ImGui.SameLine();
                    if (ImGui.Button($"{browseLabel}##open_folder_button", new Vector2(browseWidth, 0)))
                    {
                        var dir = FileDialogHelpers.GetDirectory(fileName);
                        if (Directory.Exists(dir))
                            System.Diagnostics.Process.Start("explorer.exe", dir);
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
                    if (ImGui.RadioButton("Manual##record_mode_manual", recordingMode == RecordingMode.Manual))
                    {
                        recordingMode = RecordingMode.Manual;
                    }
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Timed Recording##record_mode_timed", recordingMode == RecordingMode.TimedRecording))
                    {
                        recordingMode = RecordingMode.TimedRecording;
                    }
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Trigger##record_mode_trigger", recordingMode == RecordingMode.Trigger))
                    {
                        recordingMode = RecordingMode.Trigger;
                    }
                    if (recordButton) ImGui.EndDisabled();

                    var recordingSettingsHeight = ImGui.GetFrameHeightWithSpacing() * 3 + ImGui.GetStyle().ItemSpacing.Y * 2;

                    if (ImGui.BeginChild("##recording_settings", new Vector2(-1, recordingSettingsHeight), ImGuiChildFlags.None))
                    {
                        if (recordingMode == RecordingMode.TimedRecording)
                        {
                            if (recordButton) ImGui.BeginDisabled();

                            if (ImGui.BeginTable("##record_duration_table", 2, ImGuiTableFlags.SizingStretchSame))
                            {
                                ImGui.TableNextColumn();
                                if (automaticRestart) ImGui.BeginDisabled();
                                ImGui.Checkbox("Total Duration##use_total_duration", ref useTotalDuration);
                                if (automaticRestart) ImGui.EndDisabled();

                                ImGui.TableNextColumn();
                                if (useTotalDuration) ImGui.BeginDisabled();
                                ImGui.Checkbox("Auto Restart##automatic_restart", ref automaticRestart);
                                if (ImGui.BeginItemTooltip())
                                {
                                    ImGui.Text("When enabled, a new recording starts automatically each time the\nrecording duration elapses, until you press Stop Recording.");
                                    ImGui.EndTooltip();
                                }
                                if (useTotalDuration) ImGui.EndDisabled();

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

                                ImGui.TableNextColumn();
                                if (useTotalDuration)
                                {
                                    ImGui.AlignTextToFramePadding();
                                    ImGui.Text("Total [s]:");
                                    ImGui.SameLine();
                                    ImGui.SetNextItemWidth(-1f);
                                    if (ImGui.InputInt("##total_duration", ref totalDurationSeconds, 0, 0, ImGuiInputTextFlags.AutoSelectAll))
                                    {
                                        totalDurationSeconds = Math.Max(1, totalDurationSeconds);
                                    }
                                }

                                ImGui.TableNextColumn();
                                if (useTotalDuration && recordingDurationSeconds > 0)
                                {
                                    ImGui.AlignTextToFramePadding();
                                    int filesCount = (int)Math.Ceiling((double)totalDurationSeconds / recordingDurationSeconds);
                                    var endTime = (recordingStart ?? DateTime.Now) + TimeSpan.FromSeconds(totalDurationSeconds);
                                    ImGui.Text($"{filesCount} file{(filesCount == 1 ? "" : "s")} · ends {endTime:HH:mm:ss}");
                                }

                                ImGui.EndTable();
                            }

                            if (recordButton) ImGui.EndDisabled();
                        }
                        else if (recordingMode == RecordingMode.Trigger)
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
                        string recordLabel = recordingMode != RecordingMode.Trigger
                            ? (recordButton ? "Stop Recording##record_button" : "Record##record_button")
                            : (recordButton ? "Disarm##record_button" : "Arm Recording##record_button");
                        if (ImGui.Button(recordLabel, recordButtonSize))
                        {
                            if (string.IsNullOrEmpty(fileName))
                            {
                                if (saveDialogTask == null || saveDialogTask.IsCompleted)
                                {
                                    shouldStartRecordingWhenCompleted = true;
                                    saveDialogTask = CreateSaveFileDialogTask(fileName);
                                }
                            }
                            else
                            {
                                recordButton = !recordButton;
                            }
                        }
                        if (!AcquisitionStatus) ImGui.EndDisabled();
                    }

                    ImGui.EndChild();
                    layout = layout with { RecordingSectionHeight = ImGui.GetItemRectSize().Y };
                }
                else
                {
                    layout = layout with { RecordingSectionHeight = 0f };
                }

                if (!layout.ImageExpanded)
                    ImGui.EndChild(); // closes the shared sidebar child opened by SettingsPanel

                recordRequested = recordButton;

                var updatedFileSettings = new FileSettings
                {
                    RecordButton = recordButton,
                    RecordingMode = recordingMode,
                    CompressVideo = isCompressed,
                    FileName = fileName,
                    Suffix = suffix,
                    RecordingDuration = recordingDurationSeconds,
                    TotalDuration = totalDurationSeconds,
                    UseTotalDuration = useTotalDuration,
                    UseRecordDuration = recordingMode == RecordingMode.TimedRecording,
                    TriggerInput = triggerInput,
                    AutomaticRestart = automaticRestart,
                };

                observer.OnNext(Tuple.Create(layout, updatedFileSettings));
            },
            observer.OnError,
            observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }

    static Task<string> CreateSaveFileDialogTask(string fileName) => FileDialogHelpers.RunDialogTask(() => new SaveFileDialog
    {
        InitialDirectory = FileDialogHelpers.GetDirectory(fileName),
        Filter = "All Files|*.*",
        Title = "Choose a filename template and a folder to save Miniscope data.",
        AddExtension = false,
        CheckFileExists = false,
        CheckPathExists = false,
        FileName = Path.GetFileName(fileName)
    },
    (dlg) => (dlg as SaveFileDialog).FileName);

    internal static (float selectWidth, float browseWidth, float inputWidth) CalculateFileNameInputWidth(string selectLabel, string browseLabel)
    {
        float selectWidth = ImGui.CalcTextSize(selectLabel).X + ImGui.GetStyle().FramePadding.X * 2f;
        float browseWidth = ImGui.CalcTextSize(browseLabel).X + ImGui.GetStyle().FramePadding.X * 2f;
        return (selectWidth,
            browseWidth,
            ImGui.GetContentRegionAvail().X - selectWidth - browseWidth - ImGui.GetStyle().ItemSpacing.X * 2f);
    }
}
