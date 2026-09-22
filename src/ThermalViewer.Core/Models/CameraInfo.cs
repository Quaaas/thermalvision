namespace ThermalViewer.Core.Models;

public sealed record CameraInfo(string Model, string FirmwareVersion, string HardwareVersion, string SerialNumber)
{
    public override string ToString() => $"Model {Model}, FW {FirmwareVersion}";
}
