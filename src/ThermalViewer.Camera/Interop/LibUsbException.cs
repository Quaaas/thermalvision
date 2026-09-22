namespace ThermalViewer.Camera.Interop;

/// <summary>A libusb call returned a negative error code.</summary>
public sealed class LibUsbException : IOException
{
    public LibUsbException(string operation, int errorCode)
        : base($"{operation} failed: {NativeMethods.ErrorName(errorCode)} ({errorCode}).{Hint(errorCode)}")
    {
        Operation = operation;
        ErrorCode = errorCode;
    }

    public string Operation { get; }

    /// <summary>The raw (negative) <c>libusb_error</c> value.</summary>
    public int ErrorCode { get; }

    /// <summary>Throws if <paramref name="result"/> is a libusb error code, otherwise returns it.</summary>
    internal static int ThrowOnError(int result, string operation) =>
        result < 0 ? throw new LibUsbException(operation, result) : result;

    private static string Hint(int errorCode) => errorCode switch
    {
        LibUsbError.Access => " No permission — on Linux install the udev rule from deploy/linux and re-plug the camera.",
        LibUsbError.Busy => " Another driver or program holds the interface.",
        LibUsbError.NoDevice => " The camera was disconnected.",
        _ => string.Empty,
    };
}
