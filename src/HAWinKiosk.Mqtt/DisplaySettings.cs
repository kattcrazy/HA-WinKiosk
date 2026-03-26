using System.Runtime.InteropServices;
using Microsoft.Win32;

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
    internal const int DmPelsWidth = 0x00080000;
    internal const int DmPelsHeight = 0x00100000;
    private const string AutoRotationKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\AutoRotation";

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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetAutoRotationState(out uint pState);

    private static bool TryGetAutoRotationEnabled(out bool enabled)
    {
        enabled = false;
        try
        {
            if (!GetAutoRotationState(out var state))
                return false;

            // AR_ENABLED is represented as 0 (no disabling flags set).
            enabled = state == 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetRotationLock(bool lockOn)
    {
        // Lock ON => disable auto-rotation (Enable = 0)
        var enableValue = lockOn ? 0 : 1;
        var wrote = false;

        try
        {
            using var hkcu = Registry.CurrentUser.CreateSubKey(AutoRotationKey, true);
            hkcu?.SetValue("Enable", enableValue, RegistryValueKind.DWord);
            wrote = hkcu != null;
        }
        catch
        {
        }

        try
        {
            using var hklm = Registry.LocalMachine.OpenSubKey(AutoRotationKey, true);
            hklm?.SetValue("Enable", enableValue, RegistryValueKind.DWord);
            wrote = wrote || hklm != null;
        }
        catch
        {
        }

        return wrote;
    }

    /// <summary>DMDO_* values: 0 default, 1 90°, 2 180°, 3 270° (clockwise).</summary>
    internal static void SetPrimaryOrientation(uint dmdo)
    {
        if (dmdo > 3)
            throw new ArgumentOutOfRangeException(nameof(dmdo));

        var restoreAutoRotate = false;
        if (TryGetAutoRotationEnabled(out var wasAutoRotateEnabled) && wasAutoRotateEnabled)
        {
            if (TrySetRotationLock(lockOn: true))
            {
                restoreAutoRotate = true;
                Thread.Sleep(120);
            }
        }

        try
        {
            var dm = new DevModeW
            {
                dmDeviceName = new string('\0', 31),
                dmFormName = new string('\0', 31)
            };
            dm.dmSize = (ushort)Marshal.SizeOf<DevModeW>();

            if (!EnumDisplaySettingsW(null, EnumCurrentSettings, ref dm))
                throw new InvalidOperationException("Could not read current display settings.");

            // For 90/270 transitions, Windows expects width/height to be swapped.
            var rotateBetweenPortraitLandscape = (dm.dmDisplayOrientation + dmdo) % 2 == 1;
            if (rotateBetweenPortraitLandscape)
            {
                (dm.dmPelsWidth, dm.dmPelsHeight) = (dm.dmPelsHeight, dm.dmPelsWidth);
                dm.dmFields |= (DmPelsWidth | DmPelsHeight);
            }

            dm.dmDisplayOrientation = dmdo;
            dm.dmFields |= DmDisplayOrientation;

            var r = ChangeDisplaySettingsW(ref dm, (uint)(CdsUpdateRegistry | CdsGlobal));
            if (r != DispChangeSuccessful && r != DispChangeRestart)
                throw new InvalidOperationException($"ChangeDisplaySettings failed with code {r}. The mode may be unsupported on this GPU.");
        }
        finally
        {
            if (restoreAutoRotate)
            {
                _ = TrySetRotationLock(lockOn: false);
            }
        }
    }
}
