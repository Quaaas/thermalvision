namespace ThermalViewer.Core.Models;

/// <summary>
/// Describes a supported thermal camera by its USB identification.
/// See the project status documentation for details on the camera currently in use.
/// </summary>
/// <param name="Name">Display name, e.g. "Vantrue POWER TS1".</param>
/// <param name="VendorId">USB Vendor ID (VID).</param>
/// <param name="ProductId">USB Product ID (PID).</param>
public sealed record CameraDescriptor(string Name, int VendorId, int ProductId)
{
    /// <summary>
    /// Vantrue POWER TS1 (OEM chip from "Thermal Master Co.,Ltd").
    /// VID:PID 3474:45e1, UVC 1.10 compliant, no official VC_EXTENSION_UNITs.
    /// </summary>
    public static readonly CameraDescriptor VantruePowerTs1 = new("Vantrue POWER TS1", 0x3474, 0x45e1);

    public override string ToString() => $"{Name} ({VendorId:x4}:{ProductId:x4})";
}
