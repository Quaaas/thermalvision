using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThermalViewer.App.Rendering;
using ThermalViewer.Camera;
using ThermalViewer.Core.Imaging;
using ThermalViewer.Core.Models;
using ThermalViewer.Core.Processing;

namespace ThermalViewer.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>Index of the center pixel in the 256x192 temperature block.</summary>
    private const int CenterPixelIndex =
        (FrameSplitter.NativeImageHeight / 2 * FrameSplitter.Width) + (FrameSplitter.Width / 2);

    private const int ImageWidth = FrameSplitter.Width;
    private const int ImageHeight = FrameSplitter.NativeImageHeight;

    private readonly DispatcherTimer _statisticsTimer;
    private IThermalCameraSource? _camera;

    // Live image pipeline: colorized on the camera's dispatcher thread into _pixels, then
    // copied into a bitmap on the UI thread. _renderPending drops frames while the UI thread
    // hasn't picked up the previous one yet (no backlog, and _pixels is never written while
    // the UI thread reads it).
    private ThermalBitmapPresenter? _presenter;
    private byte[] _pixels = [];
    private AutoTemperatureRange _colorRange = new();
    private int _renderPending;

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
    private WriteableBitmap? _thermalImage;

    [ObservableProperty]
    private string _temperatureText = string.Empty;

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

        _presenter ??= new ThermalBitmapPresenter(ImageWidth, ImageHeight);
        _pixels = new byte[_presenter.BufferSize];
        _colorRange = new AutoTemperatureRange();
        Volatile.Write(ref _renderPending, 0);

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

    /// <summary>Runs on the camera's dispatcher thread — colorize here, touch UI state only via the dispatcher.</summary>
    private void OnFrameReceived(object? sender, ThermalFrame frame)
    {
        if (frame.RawTemperatureData is not { } rawData)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _renderPending, 1, 0) != 0)
        {
            return; // UI thread still busy with the previous frame
        }

        ReadOnlySpan<ushort> raw = rawData.Span;
        RawExtremes extremes = ThermalImageRenderer.FindExtremes(raw);
        (double low, double high) = _colorRange.Update(extremes.Min, extremes.Max);
        ThermalImageRenderer.Render(raw, low, high, ThermalPalettes.Ironbow, MemoryMarshal.Cast<byte, uint>(_pixels.AsSpan()));

        string text =
            $"Min {TemperatureDecoder.ToCelsius(extremes.Min):F1} °C   " +
            $"Max {TemperatureDecoder.ToCelsius(extremes.Max):F1} °C   " +
            $"Center {TemperatureDecoder.ToCelsius(raw[CenterPixelIndex]):F1} °C";
        byte[] pixels = _pixels;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_presenter is not null)
                {
                    ThermalImage = _presenter.Present(pixels);
                    TemperatureText = text;
                }
            }
            finally
            {
                Volatile.Write(ref _renderPending, 0);
            }
        });
    }

    private void UpdateStreamingStatus()
    {
        StreamStatistics? statistics = _camera?.GetStreamStatistics();
        if (statistics is null)
        {
            return;
        }

        StatusText = $"Streaming: {statistics}";
    }
}
