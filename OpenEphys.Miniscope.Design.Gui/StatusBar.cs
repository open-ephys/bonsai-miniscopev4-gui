using Bonsai;
using Hexa.NET.ImGui;
using System;
using System.ComponentModel;
using System.Numerics;
using System.Reactive;
using System.Reactive.Linq;
using System.Xml.Serialization;

namespace OpenEphys.Miniscope.Design.Gui;

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

    Vector4 colorError = new(0.9f, 0.3f, 0.3f, 1f);

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

            var sourceObserver = Observer.Create<Tuple<TSource, StatusBarDto>>(value =>
            {
                var dto = value.Item2;
                var cameraIndex = dto.CameraIndex;
                var isConnected = dto.IsConnected;
                var statusMessage = dto.StatusMessage;
                var recordingError = dto.RecordingError;

                if (!string.IsNullOrEmpty(statusMessage))
                {
                    isConnected = false; // NB: Force disconnection when there is an error message
                }

                var style = ImGui.GetStyle();
                var indexInputWidth = 60f;

                ImGui.AlignTextToFramePadding();
                ImGui.Text("Index: ");
                ImGui.SameLine();

                if (isConnected)
                    ImGui.BeginDisabled();

                ImGui.SetNextItemWidth(indexInputWidth);
                if (ImGui.InputInt("##statusbar_index", ref cameraIndex, 0, 0))
                {
                    statusMessage = string.Empty;
                }

                if (isConnected)
                    ImGui.EndDisabled();

                ImGui.SameLine();
                if (ImGui.Button(isConnected ? "Stop Acquisition##statusbar_btn" : "Start Acquisition##statusbar_btn"))
                {
                    isConnected = !isConnected;
                    statusMessage = string.Empty;
                }

                ImGui.SameLine();

                var hasError = !string.IsNullOrEmpty(statusMessage);
                var statusColor = hasError
                    ? colorError
                    : isConnected
                        ? new Vector4(0.2f, 0.8f, 0.2f, 1f)
                        : new Vector4(0.6f, 0.6f, 0.6f, 1f);
                ImGui.PushStyleColor(ImGuiCol.Text, statusColor);
                var displayStatus = hasError
                    ? statusMessage
                    : isConnected ? "Acquiring" : "Disconnected";
                ImGui.Text(displayStatus);
                ImGui.PopStyleColor();

                if (!string.IsNullOrEmpty(recordingError))
                {
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.3f, 0.3f, 1f));
                    ImGui.Text($"| Save error: {recordingError}");
                    ImGui.PopStyleColor();
                }

                if (ImGui.BeginTable("##status_bar", 5))
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

                    ImGui.EndTable();
                }

                ImGui.Separator();

                observer.OnNext(Tuple.Create(value.Item1, new StatusBarDto(cameraIndex, isConnected, statusMessage, recordingError)));
            },
            observer.OnError,
            observer.OnCompleted);

            return source.SubscribeSafe(sourceObserver);
        });
    }
}
