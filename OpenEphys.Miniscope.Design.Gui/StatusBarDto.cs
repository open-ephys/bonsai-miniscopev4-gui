using System;
using System.ComponentModel;
using System.Reactive.Linq;
using Bonsai;

namespace OpenEphys.Miniscope.Design.Gui;

/// <summary>
/// Represents the state displayed and edited by the status bar.
/// </summary>
/// <param name="CameraIndex">The index of the camera to connect to.</param>
/// <param name="IsConnected">Whether the miniscope is currently connected.</param>
public record StatusBarDto(int CameraIndex, bool IsConnected);

/// <summary>
/// Combines individual status bar values into a single <see cref="StatusBarDto"/>.
/// </summary>
[Description("Combines individual status bar values into a single object.")]
[Combinator]
public class CreateStatusBarDto
{
    /// <summary>
    /// Creates a <see cref="StatusBarDto"/> by combining the latest values from each individual status bar sequence.
    /// </summary>
    /// <param name="cameraIndex">The index of the camera to connect to.</param>
    /// <param name="isConnected">Whether the miniscope is currently connected.</param>
    /// <returns>A sequence of <see cref="StatusBarDto"/> objects.</returns>
    public IObservable<StatusBarDto> Process(
        IObservable<int> cameraIndex,
        IObservable<bool> isConnected)
    {
        return Observable.CombineLatest(
            cameraIndex,
            isConnected,
            (cameraIndex, isConnected) => new StatusBarDto(cameraIndex, isConnected));
    }
}
