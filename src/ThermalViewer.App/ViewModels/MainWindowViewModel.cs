using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThermalViewer.Camera;
using ThermalViewer.Core.Models;

namespace ThermalViewer.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "Camera not connected.";

    [ObservableProperty]
    private bool _isConnected;

    private IThermalCameraSource? _camera;

    [RelayCommand]
    private async Task ConnectAsync()
    {
        try
        {
            _camera = new UsbCameraDevice(CameraDescriptor.VantruePowerTs1);
            await _camera.OpenAsync();
            IsConnected = true;
            StatusText = $"Connected to {CameraDescriptor.VantruePowerTs1}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Connection failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task TriggerShutterCalibrationAsync()
    {
        if (_camera is null)
        {
            return;
        }

        try
        {
            await _camera.TriggerShutterCalibrationAsync();
            StatusText = "Shutter calibration triggered.";
        }
        catch (Exception ex)
        {
            StatusText = $"Shutter command failed: {ex.Message}";
        }
    }
}
