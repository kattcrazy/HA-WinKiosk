using System.Runtime.InteropServices;

namespace HAWinKiosk.Mqtt;

/// <summary>
/// Primary display rotation and brightness helpers (single-display kiosk; not for multi-monitor layouts).
/// </summary>
internal static class DisplaySettings
{
    internal const int EnumCurrentSettings = -1;
    internal const int CdsUpdateRegistry = 0x00000001;
    internal const int CdsGlobal = 0x00000008;
    internal const int DispChangeSuccessful = 0;
    internal const int DispChangeRestart = 1;
    internal const int DmDisplayOrientation = 0x00000200;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DevModeW
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public short dmOrientation;
        public short dmPaperSize;
        public short dmPaperLength;
        public short dmPaperWidth;
        public short dmScale;
        public short dmCopies;
        public short dmDefaultSource;
        public short dmPrintQuality;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool EnumDisplaySettingsW(string? deviceName, int modeNum, ref DevModeW devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int ChangeDisplaySettingsW(ref DevModeW devMode, uint flags);

    /// <summary>DMDO_* values: 0 default, 1 90°, 2 180°, 3 270° (clockwise).</summary>
    internal static void SetPrimaryOrientation(uint dmdo)
    {
        if (dmdo > 3)
            throw new ArgumentOutOfRangeException(nameof(dmdo));

        var dm = new DevModeW
        {
            dmDeviceName = new string('\0', 31),
            dmFormName = new string('\0', 31)
        };
        dm.dmSize = (ushort)Marshal.SizeOf<DevModeW>();

        if (!EnumDisplaySettingsW(null, EnumCurrentSettings, ref dm))
            throw new InvalidOperationException("Could not read current display settings.");

        dm.dmDisplayOrientation = dmdo;
        dm.dmFields |= DmDisplayOrientation;

        var r = ChangeDisplaySettingsW(ref dm, (uint)(CdsUpdateRegistry | CdsGlobal));
        if (r != DispChangeSuccessful && r != DispChangeRestart)
            throw new InvalidOperationException($"ChangeDisplaySettings failed with code {r}. The mode may be unsupported on this GPU.");
    }
}
