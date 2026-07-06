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
    /// Gets or sets a value indicating whether a recording is currently in progress.
    /// </summary>
    [XmlIgnore]
    [Browsable(false)]
    public bool RecordingStatus { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an automatic restart was triggered.
    /// </summary>
    /// <remarks>
    /// Automatic restarts are not guaranteed to reset the recording timer; this value
    /// can be set to force a reset of the recording timer.
    /// </remarks>
    [XmlIgnore]
    [Browsable(false)]
    public bool AutomaticRestartTriggered { get; set; }

    static readonly Vector4 colorStart = new(0.15f, 0.55f, 0.20f, 1f);
    static readonly Vector4 colorStartHovered = new(0.20f, 0.67f, 0.25f, 1f);
    static readonly Vector4 colorStop = new(0.70f, 0.20f, 0.20f, 1f);
    static readonly Vector4 colorStopHovered = new(0.82f, 0.25f, 0.25f, 1f);

    /// <summary>
    /// Renders the status bar controls and returns an updated <see cref="CameraStatus"/> alongside each source value.
    /// </summary>
    /// <param name="source">A sequence of values tied to the render tick of DearImGui.</param>
    /// <returns>A sequence of values paired with the status bar state updated from the rendered controls.</returns>
    public IObservable<Tuple<TSource, CameraStatus>> Process<TSource>(IObservable<Tuple<TSource, CameraStatus>> source)
    {
        double elapsedAcquisitionTime = 0;

        return Observable.Create<Tuple<TSource, CameraStatus>>(observer =>
        {
            DateTime? acquisitionStart = null;
            DateTime? recordingStart = null;

            var sourceObserver = Observer.Create<Tuple<TSource, CameraStatus>>(value =>
            {
                var dto = value.Item2;
                var cameraIndex = dto.CameraIndex;
                var isConnected = dto.IsConnected;

                if (AutomaticRestartTriggered)
                {
                    recordingStart = null;
                    AutomaticRestartTriggered = false;
                }

                if (ImGui.BeginTable("##statusbar", 2))
                {
                    ImGui.TableNextColumn();

                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Index: ");
                    ImGui.SameLine();

                    if (isConnected)
                        ImGui.BeginDisabled();

                    ImGui.SetNextItemWidth(60f * UiScale.Current);
                    ImGui.InputInt("##statusbar_index", ref cameraIndex, 0, 0);

                    if (isConnected)
                        ImGui.EndDisabled();

                    ImGui.SameLine();
                    var acqColor = isConnected ? colorStop : colorStart;
                    var acqColorHovered = isConnected ? colorStopHovered : colorStartHovered;
                    ImGui.PushStyleColor(ImGuiCol.Button, acqColor);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, acqColorHovered);
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, acqColor);
                    var acqButtonSize = new Vector2(140f * UiScale.Current, 0f);
                    if (ImGui.Button(isConnected ? "Stop Acquisition##statusbar_btn" : "Start Acquisition##statusbar_btn", acqButtonSize))
                    {
                        isConnected = !isConnected;
                    }
                    ImGui.PopStyleColor(3);

                    var statusColor = isConnected
                            ? new Vector4(0.2f, 0.8f, 0.2f, 1f)
                            : new Vector4(0.6f, 0.6f, 0.6f, 1f);
                    ImGui.PushStyleColor(ImGuiCol.Text, statusColor);
                    ImGui.SameLine();
                    ImGui.Text(isConnected ? "Acquiring" : "Disconnected");
                    ImGui.PopStyleColor();

                    ImGui.TableNextColumn();

                    if (ImGui.BeginTable("##status_timers", 2))
                    {
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

                        ImGui.EndTable();
                    }
                    
                    ImGui.EndTable();
                }

                ImGui.Separator();

                observer.OnNext(Tuple.Create(value.Item1, new CameraStatus(cameraIndex, isConnected)));
            },
            observer.OnError,
            observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }
}
