using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThermalViewer.Camera;
using ThermalViewer.Core.Models;
using ThermalViewer.Core.Processing;

namespace ThermalViewer.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>Index of the center pixel in the 256x192 temperature block.</summary>
    private const int CenterPixelIndex =
        (FrameSplitter.NativeImageHeight / 2 * FrameSplitter.Width) + (FrameSplitter.Width / 2);

    private readonly DispatcherTimer _statisticsTimer;
    private IThermalCameraSource? _camera;
    private volatile string _lastFrameSummary = string.Empty;

    public MainWindowViewModel()
    {
        _statisticsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statisticsTimer.Tick += (_, _) => UpdateStreamingStatus();
    }

    [ObservableProperty]
    private CameraInfo? _cameraInfo;

    [ObservableProperty]
    private string _statusText = "Camera not connected.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(TriggerShutterCalibrationCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartStreamingCommand))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartStreamingCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopStreamingCommand))]
    private bool _isStreaming;

    private bool CanConnect() => !IsConnected;

    private bool CanStartStreaming() => IsConnected && !IsStreaming;

    private bool CanStopStreaming() => IsStreaming;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        var camera = new UsbCameraDevice(CameraDescriptor.VantruePowerTs1);
        try
        {
            await camera.OpenAsync();
        }
        catch (Exception ex)
        {
            await camera.DisposeAsync();
            StatusText = $"Connection failed: {ex.Message}";
            return;
        }

        _camera = camera;
        IsConnected = true;
        StatusText = $"Connected to {CameraDescriptor.VantruePowerTs1}.";

        try
        {
            CameraInfo = await camera.ReadDeviceInfoAsync();
            StatusText = $"Connected: {CameraInfo}";
        }
        catch (Exception ex)
        {
            CameraInfo = null;
            StatusText = $"Connected, but device info not readable: {ex.Message}";
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
            if (!IsStreaming)
            {
                StatusText = "Shutter calibration triggered.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Shutter command failed: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartStreaming))]
    private async Task StartStreamingAsync()
    {
        if (_camera is null)
        {
            return;
        }

        _lastFrameSummary = string.Empty;
        _camera.FrameReceived += OnFrameReceived;
        try
        {
            await _camera.StartStreamingAsync(ThermalFrameFormat.ImageWithRawTemperature256X386);
        }
        catch (Exception ex)
        {
            _camera.FrameReceived -= OnFrameReceived;
            StatusText = $"Streaming failed: {ex.Message}";
            return;
        }

        IsStreaming = true;
        StatusText = "Streaming started, waiting for frames…";
        _statisticsTimer.Start();
    }

    [RelayCommand(CanExecute = nameof(CanStopStreaming))]
    private async Task StopStreamingAsync()
    {
        if (_camera is null)
        {
            return;
        }

        _statisticsTimer.Stop();
        UpdateStreamingStatus(); // final numbers
        string finalStatus = StatusText;

        try
        {
            await _camera.StopStreamingAsync();
            StatusText = $"Stopped. {finalStatus}";
        }
        catch (Exception ex)
        {
            StatusText = $"Stopping failed: {ex.Message}";
        }
        finally
        {
            _camera.FrameReceived -= OnFrameReceived;
            IsStreaming = false;
        }
    }

    /// <summary>Runs on the camera's dispatcher thread — only store a summary, no UI access.</summary>
    private void OnFrameReceived(object? sender, ThermalFrame frame)
    {
        if (frame.RawTemperatureData is { } raw)
        {
            ushort center = raw.Span[CenterPixelIndex];
            _lastFrameSummary = $"center {TemperatureDecoder.ToCelsius(center):F1} °C (raw {center})";
        }
    }

    private void UpdateStreamingStatus()
    {
        StreamStatistics? statistics = _camera?.GetStreamStatistics();
        if (statistics is null)
        {
            return;
        }

        StatusText = string.IsNullOrEmpty(_lastFrameSummary)
            ? $"Streaming: {statistics}"
            : $"Streaming: {_lastFrameSummary} — {statistics}";
    }
}
