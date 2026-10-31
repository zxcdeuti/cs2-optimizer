using System.Runtime.InteropServices;

namespace CS2Optimizer.Services;

/// <summary>Windows pointer settings that matter for aim in menus and on the desktop.</summary>
public static class MouseSettings
{
    const uint SpiGetMouse = 0x0003, SpiSetMouse = 0x0004, SpiGetMouseSpeed = 0x0070;
    const uint UpdateIniFile = 0x01, SendChange = 0x02;

    /// <summary>"Enhance pointer precision" — Windows mouse acceleration.</summary>
    public static bool AccelerationEnabled
    {
        get
        {
            var values = new int[3];
            return SystemParametersInfo(SpiGetMouse, 0, values, 0) && values[2] != 0;
        }
    }

    public static bool DisableAcceleration() =>
        SystemParametersInfo(SpiSetMouse, 0, new int[3], UpdateIniFile | SendChange);

    /// <summary>Pointer speed 1–20; 10 is the 1:1 default.</summary>
    public static int PointerSpeed
    {
        get
        {
            int speed = 0;
            return SystemParametersInfo(SpiGetMouseSpeed, 0, ref speed, 0) ? speed : 10;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, int[] values, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint winIni);
}
