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
public record CameraStatus(int CameraIndex, bool IsConnected);

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
    /// <returns>A sequence of <see cref="CameraStatus"/> objects.</returns>
    public IObservable<CameraStatus> Process(
        IObservable<int> cameraIndex,
        IObservable<bool> isConnected)
    {
        return Observable.CombineLatest(
            cameraIndex,
            isConnected,
            (cameraIndex, isConnected) => new CameraStatus(cameraIndex, isConnected));
    }
}
