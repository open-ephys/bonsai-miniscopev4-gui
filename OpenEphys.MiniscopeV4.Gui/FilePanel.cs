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

    static readonly string RecordButtonLabelText = " (Ctrl+R)##record_button";

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
                var segmentMode = fileSettings.SegmentMode;
                bool isCompressed = fileSettings.CompressVideo;
                var triggerInput = fileSettings.TriggerInput;
                int triggerIndex = Array.IndexOf(DigitalInValues, triggerInput);

                bool recordButtonActive = AcquisitionStatus;

                void RecordButtonPressed()
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

                if (recordButtonActive && !ImGui.GetIO().WantTextInput && ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.R))
                    RecordButtonPressed();

                if (triggerIndex < 1) triggerIndex = 1;

                if (recordButton)
                {
                    recordingStart ??= DateTime.Now;
                }
                else if (recordingStart != null)
                {
                    recordingStart = null;
                }

                if (!layout.ImageExpanded && layout.SidebarOpen)
                {
                    ImGui.BeginChild("##file_pane", new Vector2(-1f, 0f), ImGuiChildFlags.AutoResizeY);

                    ImGui.Separator();
                    ImGui.Text("Recording");
                    ImGui.Dummy(new Vector2(0f, ImGui.GetStyle().ItemSpacing.Y));

                    ImGui.Text("Data Path");

                    const string selectLabel = "...";
                    const string browseLabel = "Browse";
                    var (selectWidth, browseWidth, inputWidth) = CalculateFileNameInputWidth(selectLabel, browseLabel);

                    ImGui.SetNextItemWidth(inputWidth);
                    ImGui.InputText("##filename", ref fileName, bufSize, ImGuiInputTextFlags.ElideLeft);
                    if (Tooltip.Begin())
                    {
                        Tooltip.AddLine("The data path used to save all files: a folder plus a base filename.");
                        Tooltip.AddLine("The selected suffix (if any) is inserted after the base filename and before the extension.");
                        Tooltip.AddLine($"Video files get '{GenerateRecordingFileNames.ImageExtension}', data files get '{GenerateRecordingFileNames.CsvExtension}', logs get '{GenerateRecordingFileNames.LogExtension}', configuration files get '{GenerateRecordingFileNames.ConfigExtension}' appended automatically.");
                        Tooltip.End();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button($"{selectLabel}##choose_filename_button", new Vector2(selectWidth, 0)))
                    {
                        if (saveDialogTask == null || saveDialogTask.IsCompleted)
                        {
                            saveDialogTask = CreateSaveFileDialogTask(fileName);
                        }
                    }
                    Tooltip.Describe("Specify a save location and base filename for all data.");

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
                    Tooltip.Describe("Open the data folder in File Explorer to browse for previously saved data files.");

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

                        if (Tooltip.Begin())
                        {
                            Tooltip.AddLine("Text appended to each filename to keep successive recordings unique:");
                            Tooltip.AddLine("- None leaves the name as-is.");
                            Tooltip.AddLine("- FileCount adds an incrementing number.");
                            Tooltip.AddLine("- Timestamp adds the recording's date and time.");
                            Tooltip.End();
                        }

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(-1f);
                        ImGui.Checkbox("Compress Video##compress_video", ref isCompressed);
                        if (Tooltip.Begin())
                        {
                            Tooltip.AddLine("Encode saved video with compression to reduce file size, at the cost of higher CPU usage during recording.");
                            Tooltip.AddLine("Videos are saved using the 'Y800' codec when disabled, or the 'MJPG' codec when enabled.");
                            Tooltip.End();
                        }

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
                    RecordModeTooltip("Start and stop recording manually with the Record button.", recordButton);
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Segmented##record_mode_segmented", recordingMode == RecordingMode.Segmented))
                    {
                        recordingMode = RecordingMode.Segmented;
                    }
                    RecordModeTooltip("Record in segments of a fixed duration, as a single file, split into multiple files, or restarted automatically.", recordButton);
                    ImGui.SameLine();
                    if (ImGui.RadioButton("Trigger##record_mode_trigger", recordingMode == RecordingMode.Trigger))
                    {
                        recordingMode = RecordingMode.Trigger;
                    }
                    RecordModeTooltip("Arm recording so a digital input enables recording while the input is high.", recordButton);
                    if (recordButton) ImGui.EndDisabled();

                    var recordingSettingsHeight = ImGui.GetFrameHeightWithSpacing() * 3 + ImGui.GetStyle().ItemSpacing.Y * 2;

                    if (ImGui.BeginChild("##recording_settings", new Vector2(-1, recordingSettingsHeight), ImGuiChildFlags.None))
                    {
                        if (recordingMode == RecordingMode.Segmented)
                        {
                            if (recordButton) ImGui.BeginDisabled();

                            if (ImGui.BeginTable("##record_duration_table", 2))
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
                                Tooltip.Describe("Length of each recording file, in seconds.");

                                ImGui.EndTable();
                            }

                            if (ImGui.RadioButton("Single File##segment_mode_single_file", segmentMode == SegmentMode.SingleFile))
                            {
                                segmentMode = SegmentMode.SingleFile;
                            }
                            Tooltip.Describe("Record to a single file until the duration is reached.");
                            ImGui.SameLine();
                            if (ImGui.RadioButton("Total Duration##segment_mode_total_duration", segmentMode == SegmentMode.TotalDuration))
                            {
                                segmentMode = SegmentMode.TotalDuration;
                            }
                            Tooltip.Describe("Split a long recording into successive files of the duration above, stopping once the total duration is reached.");
                            ImGui.SameLine();
                            if (ImGui.RadioButton("Auto Restart##segment_mode_auto_restart", segmentMode == SegmentMode.AutoRestart))
                            {
                                segmentMode = SegmentMode.AutoRestart;
                            }
                            Tooltip.Describe("Automatically start a new recording each time the recording duration elapses, until you press Stop Recording.");

                            if (segmentMode == SegmentMode.TotalDuration)
                            {
                                if (ImGui.BeginTable("##total_duration_table", 2))
                                {
                                    ImGui.TableNextColumn();
                                    ImGui.AlignTextToFramePadding();
                                    ImGui.Text("Total [s]:");
                                    ImGui.SameLine();
                                    ImGui.SetNextItemWidth(-1f);
                                    if (ImGui.InputInt("##total_duration", ref totalDurationSeconds, 0, 0, ImGuiInputTextFlags.AutoSelectAll))
                                    {
                                        totalDurationSeconds = Math.Max(1, totalDurationSeconds);
                                    }
                                    Tooltip.Describe("Total recording length across all files, in seconds.");

                                    ImGui.TableNextColumn();
                                    if (recordingDurationSeconds > 0)
                                    {
                                        ImGui.AlignTextToFramePadding();
                                        int filesCount = (int)Math.Ceiling((double)totalDurationSeconds / recordingDurationSeconds);
                                        var endTime = (recordingStart ?? DateTime.Now) + TimeSpan.FromSeconds(totalDurationSeconds);
                                        ImGui.Text($"{filesCount} file{(filesCount == 1 ? "" : "s")} · ends {endTime:HH:mm:ss}");
                                    }

                                    ImGui.EndTable();
                                }
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
                            if (Tooltip.Begin(allowWhenDisabled: true))
                            {
                                Tooltip.AddLine("Digital input that triggers recording: recording runs only while it is high.");
                                if (recordButton) Tooltip.Note("Unavailable while armed.");
                                Tooltip.End();
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
                        if (!recordButtonActive) ImGui.BeginDisabled();

                        string recordLabel = "", tooltipLine = "";

                        if (recordingMode == RecordingMode.Manual || recordingMode == RecordingMode.Segmented)
                        {
                            if (recordButton)
                            {
                                recordLabel = "Stop Recording" + RecordButtonLabelText;
                                tooltipLine = "Stop the current recording.";
                            }
                            else
                            {
                                recordLabel = "Record" + RecordButtonLabelText;
                                tooltipLine = "Start recording to the data path.";
                            }
                        }
                        else if (recordingMode == RecordingMode.Trigger)
                        {
                            if (recordButton)
                            {
                                recordLabel = "Disarm" + RecordButtonLabelText;
                                tooltipLine = "Disarm recording.";
                            }
                            else
                            {
                                recordLabel = "Arm Recording" + RecordButtonLabelText;
                                tooltipLine = "Arm recording so the selected digital input can control it.";
                            }
                        }

                        if (ImGui.Button(recordLabel, recordButtonSize))
                        {
                            RecordButtonPressed();
                        }

                        if (Tooltip.Begin(allowWhenDisabled: true))
                        {
                            Tooltip.AddLine(tooltipLine);
                            Tooltip.AddKeyboardShortcut("Ctrl+R");
                            if (!recordButtonActive)
                                Tooltip.Note("Unavailable while acquisition is stopped.");
                            Tooltip.End();
                        }

                        if (!recordButtonActive) ImGui.EndDisabled();
                    }

                    ImGui.EndChild();
                    layout = layout with { RecordingSectionHeight = ImGui.GetItemRectSize().Y + ImGui.GetStyle().ItemSpacing.Y };
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
                    SegmentMode = segmentMode,
                    TriggerInput = triggerInput,
                };

                observer.OnNext(Tuple.Create(layout, updatedFileSettings));
            },
            observer.OnError,
            observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }

    static void RecordModeTooltip(string description, bool recording)
    {
        if (Tooltip.Begin(allowWhenDisabled: true))
        {
            Tooltip.AddLine(description);
            if (recording)
                Tooltip.Note("Unavailable while recording.");
            Tooltip.End();
        }
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
