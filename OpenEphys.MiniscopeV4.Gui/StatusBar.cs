using Bonsai;
using Hexa.NET.ImGui;
using System;
using System.ComponentModel;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Xml.Serialization;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Renders the ImGui status bar controls at the top of the GUI.
/// </summary>
[Combinator]
[Description("Renders the ImGui status bar controls at the top of the GUI.")]
public class StatusBar
{
    /// <summary>
    /// Gets or sets the average frame rate, in Hz, used to display the acquisition frame rate.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public double AverageFrameRate { get; set; }

    /// <summary>
    /// Gets or sets the frame number of the current frame.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public int FrameNumber { get; set; }

    /// <summary>
    /// Gets or sets the number of dropped frames since acquisition started.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public int DroppedFrames { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a recording is currently in progress.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public bool RecordingStatus { get; set; }

    static readonly Vector4 colorError = new(0.9f, 0.3f, 0.3f, 1f);
    static readonly Vector4 colorWarning = new(0.9f, 0.8f, 0.3f, 1f);
    static readonly Vector4 colorInfo = new(0.8f, 0.8f, 0.8f, 1f);

    static void RenderLogLines(LogEntry[] entries)
    {
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var color = entry.Level switch
            {
                LogLevel.Error => colorError,
                LogLevel.Warning => colorWarning,
                _ => colorInfo,
            };
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextUnformatted($"[{entry.Timestamp:HH:mm:ss}] {entry.Message}");
            ImGui.PopStyleColor();
        }
    }

    /// <summary>
    /// Renders the status bar controls and returns an updated <see cref="StatusBarDto"/> alongside each source value.
    /// </summary>
    /// <param name="source">A sequence of values tied to the render tick of DearImGui.</param>
    /// <returns>A sequence of values paired with the status bar state updated from the rendered controls.</returns>
    public IObservable<Tuple<TSource, StatusBarDto>> Process<TSource>(IObservable<Tuple<TSource, StatusBarDto>> source)
    {
        double elapsedAcquisitionTime = 0;

        return Observable.Create<Tuple<TSource, StatusBarDto>>(observer =>
        {
            DateTime? acquisitionStart = null;
            DateTime? recordingStart = null;

            int lastLogVersion = -1;
            int miniScrollVersion = -1;
            int fullScrollVersion = -1;
            LogEntry[] logCache = Array.Empty<LogEntry>();

            var sourceObserver = Observer.Create<Tuple<TSource, StatusBarDto>>(value =>
            {
                var dto = value.Item2;
                var cameraIndex = dto.CameraIndex;
                var isConnected = dto.IsConnected;

                var style = ImGui.GetStyle();
                var indexInputWidth = 60f;

                if (ImGui.BeginTable("##status_table", 2))
                {
                    ImGui.TableNextColumn();
                    var topY = ImGui.GetCursorScreenPos().Y;
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Index: ");
                    ImGui.SameLine();

                    if (isConnected)
                        ImGui.BeginDisabled();

                    ImGui.SetNextItemWidth(indexInputWidth);
                    ImGui.InputInt("##statusbar_index", ref cameraIndex, 0, 0);

                    if (isConnected)
                        ImGui.EndDisabled();

                    ImGui.SameLine();
                    if (ImGui.Button(isConnected ? "Stop Acquisition##statusbar_btn" : "Start Acquisition##statusbar_btn"))
                    {
                        isConnected = !isConnected;
                    }

                    var statusColor = isConnected
                            ? new Vector4(0.2f, 0.8f, 0.2f, 1f)
                            : new Vector4(0.6f, 0.6f, 0.6f, 1f);
                    ImGui.PushStyleColor(ImGuiCol.Text, statusColor);
                    ImGui.SameLine();
                    ImGui.Text(isConnected ? "Acquiring" : "Disconnected");
                    ImGui.PopStyleColor();

                    if (ImGui.BeginTable("##status_values", 3))
                    {
                        ImGui.TableNextColumn();
                        ImGui.Text($"Frames per Second: {AverageFrameRate:F1}");

                        ImGui.TableNextColumn();
                        ImGui.Text($"Frame Number: {FrameNumber}");

                        ImGui.TableNextColumn();
                        if (DroppedFrames > 0) ImGui.PushStyleColor(ImGuiCol.Text, colorError);
                        ImGui.Text($"Dropped Frames: {DroppedFrames}");
                        if (DroppedFrames > 0) ImGui.PopStyleColor();

                        ImGui.TableNextColumn();
                        if (isConnected)
                        {
                            acquisitionStart ??= DateTime.Now;
                            elapsedAcquisitionTime = (DateTime.Now - acquisitionStart.Value).TotalSeconds;
                        }
                        else if (acquisitionStart != null)
                        {
                            acquisitionStart = null;
                        }
                        ImGui.Text($"Acquiring: {elapsedAcquisitionTime:F0} s");

                        ImGui.TableNextColumn();
                        if (RecordingStatus)
                        {
                            recordingStart ??= DateTime.Now;
                            ImGui.Text($"Recording: {(DateTime.Now - recordingStart.Value).TotalSeconds:F0} s");
                        }
                        else if (recordingStart != null)
                        {
                            recordingStart = null;
                        }
                    }

                    ImGui.EndTable();

                    var capturedHeight = ImGui.GetCursorScreenPos().Y - topY;

                    ImGui.TableNextColumn();

                    int logVersion = MiniscopeLog.Version;
                    if (logVersion != lastLogVersion)
                    {
                        logCache = MiniscopeLog.Snapshot();
                        lastLogVersion = logVersion;
                    }

                    bool consoleHovered = false;
                    if (ImGui.BeginChild("##console_mini", new Vector2(-1f, capturedHeight), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar))
                    {
                        consoleHovered = ImGui.IsWindowHovered();
                        if (logCache.Length == 0)
                        {
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
                            ImGui.TextUnformatted("Console (click to expand)");
                            ImGui.PopStyleColor();
                        }
                        else
                        {
                            RenderLogLines(logCache);
                            if (logVersion != miniScrollVersion)
                            {
                                ImGui.SetScrollHereY(1f);
                                miniScrollVersion = logVersion;
                            }
                        }
                    }

                    ImGui.EndChild();

                    if (consoleHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                        ImGui.OpenPopup("##console_full");

                    ImGui.SetNextWindowSize(new Vector2(720f, 400f), ImGuiCond.Appearing);
                    if (ImGui.BeginPopup("##console_full"))
                    {
                        if (ImGui.Button("Clear##console_clear"))
                            MiniscopeLog.Clear();

                        ImGui.SameLine();
                        ImGui.TextUnformatted($"{logCache.Length} message(s)");

                        if (ImGui.BeginChild("##console_full_scroll", new Vector2(700f, 340f), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar))
                        {
                            RenderLogLines(logCache);

                            if (logVersion != fullScrollVersion)
                            {
                                ImGui.SetScrollHereY(1f);
                                fullScrollVersion = logVersion;
                            }
                        }

                        ImGui.EndChild();

                        ImGui.EndPopup();
                    }
                }

                ImGui.EndTable();

                ImGui.Separator();

                observer.OnNext(Tuple.Create(value.Item1, new StatusBarDto(cameraIndex, isConnected)));
            },
            observer.OnError,
            observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }
}
