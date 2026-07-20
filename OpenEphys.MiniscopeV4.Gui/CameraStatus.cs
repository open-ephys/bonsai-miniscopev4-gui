using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

namespace OpenEphys.MiniscopeV4.Gui;

/// <summary>
/// Represents the state displayed and edited by the status bar.
/// </summary>
/// <param name="CameraIndex">The index of the camera to connect to.</param>
/// <param name="IsConnected">Whether the miniscope is currently connected.</param>
/// <param name="Paused">
/// Whether the data display is frozen. When <see langword="true"/>, the workflow stops sampling new
/// frames into the image and signal panels while data acquisition continues unaffected. Toggled by the
/// Pause button in the <see cref="StatusBar"/>.
/// </param>
public record CameraStatus(int CameraIndex, bool IsConnected, bool Paused);

/// <summary>
/// Combines individual status bar values into a single <see cref="CameraStatus"/>.
/// </summary>
[Description("Combines individual status bar values into a single object.")]
[Combinator]
public class CreateCameraStatus
{
    /// <summary>
    /// Creates a <see cref="CameraStatus"/> by combining the latest values from each individual status bar sequence.
    /// </summary>
    /// <param name="cameraIndex">The index of the camera to connect to.</param>
    /// <param name="isConnected">Whether the miniscope is currently connected.</param>
    /// <param name="paused">Whether the data display is frozen.</param>
    /// <returns>A sequence of <see cref="CameraStatus"/> objects.</returns>
    public IObservable<CameraStatus> Process(
        IObservable<int> cameraIndex,
        IObservable<bool> isConnected,
        IObservable<bool> paused)
    {
        return Observable.CombineLatest(
            cameraIndex,
            isConnected,
            paused,
            (cameraIndex, isConnected, paused) => new CameraStatus(cameraIndex, isConnected, paused));
    }
}
