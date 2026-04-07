using System.Runtime.InteropServices;
using VoiceInputApp.Services.Logging;

namespace VoiceInputApp.Services.Hotkey;

public class HotkeyMonitor : IHotkeyMonitor
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_RSHIFT = 0xA1;
    private const int VK_LSHIFT = 0xA0;

    private readonly ILoggingService _logger = LoggingService.Instance;

    private IntPtr _hookId = IntPtr.Zero;
    private readonly HookProc _hookProc;
    private bool _isRunning;
    private bool _rightShiftPressed;
    private bool _recordingStarted;

    public event EventHandler<HotkeyEventArgs>? KeyPressed;
    public event EventHandler<HotkeyEventArgs>? KeyReleased;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    public HotkeyMonitor()
    {
        _hookProc = HookCallback;
        DisableAccessibilityFeatures();
    }

    private void DisableAccessibilityFeatures()
    {
        try
        {
            var stickyKeys = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf<STICKYKEYS>() };
            if (SystemParametersInfo(SPI_GETSTICKYKEYS, stickyKeys.cbSize, ref stickyKeys, 0))
            {
                var originalFlags = stickyKeys.dwFlags;
                stickyKeys.dwFlags &= ~(SKF_STICKYKEYSON | SKF_HOTKEYACTIVE | SKF_CONFIRMHOTKEY | SKF_TRIPLECLICK | SKF_AUDIBLESELECTION | SKF_REORDERHOTKEYS);
                if (stickyKeys.dwFlags != originalFlags)
                {
                    SystemParametersInfo(SPI_SETSTICKYKEYS, stickyKeys.cbSize, ref stickyKeys, SPIF_SENDWININICHANGE);
                    _logger.Info($"StickyKeys disabled: original=0x{originalFlags:X8}, new=0x{stickyKeys.dwFlags:X8}");
                }
            }

            var filterKeys = new FILTERKEYS { cbSize = (uint)Marshal.SizeOf<FILTERKEYS>() };
            if (SystemParametersInfo(SPI_GETFILTERKEYS, filterKeys.cbSize, ref filterKeys, 0))
            {
                var originalFlags = filterKeys.dwFlags;
                filterKeys.dwFlags &= ~(FKF_FILTERKEYSON | FKF_HOTKEYACTIVE | FKF_CONFIRMHOTKEY | FKF_CLICKON);
                if (filterKeys.dwFlags != originalFlags)
                {
                    SystemParametersInfo(SPI_SETFILTERKEYS, filterKeys.cbSize, ref filterKeys, SPIF_SENDWININICHANGE);
                    _logger.Info($"FilterKeys disabled: original=0x{originalFlags:X8}, new=0x{filterKeys.dwFlags:X8}");
                }
            }

            var toggleKeys = new TOGGLEKEYS { cbSize = (uint)Marshal.SizeOf<TOGGLEKEYS>() };
            if (SystemParametersInfo(SPI_GETTOGGLEKEYS, toggleKeys.cbSize, ref toggleKeys, 0))
            {
                var originalFlags = toggleKeys.dwFlags;
                toggleKeys.dwFlags &= ~(TKF_TOGGLEKEYSON | TKF_HOTKEYACTIVE | TKF_CONFIRMHOTKEY);
                if (toggleKeys.dwFlags != originalFlags)
                {
                    SystemParametersInfo(SPI_SETTOGGLEKEYS, toggleKeys.cbSize, ref toggleKeys, SPIF_SENDWININICHANGE);
                    _logger.Info($"ToggleKeys disabled: original=0x{originalFlags:X8}, new=0x{toggleKeys.dwFlags:X8}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to disable accessibility features: {ex.Message}");
        }
    }

    public void Start()
    {
        if (_isRunning) return;

        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
        _isRunning = true;
        _rightShiftPressed = false;
        _recordingStarted = false;
    }

    public void Stop()
    {
        if (!_isRunning) return;

        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _isRunning = false;
        _rightShiftPressed = false;
        _recordingStarted = false;
    }

    public void SetRecordingStarted(bool started)
    {
        _recordingStarted = started;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var vkCode = Marshal.ReadInt32(lParam);
            var isKeyDown = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
            var isKeyUp = wParam == WM_KEYUP || wParam == WM_SYSKEYUP;

            if (vkCode == VK_RSHIFT)
            {
                if (isKeyDown && !_rightShiftPressed)
                {
                    _rightShiftPressed = true;
                    _recordingStarted = false;
                    KeyPressed?.Invoke(this, new HotkeyEventArgs { IsRightShift = true, IsKeyDown = true });
                }
                else if (isKeyUp && _rightShiftPressed)
                {
                    _rightShiftPressed = false;
                    if (_recordingStarted)
                    {
                        KeyReleased?.Invoke(this, new HotkeyEventArgs { IsRightShift = true, IsKeyDown = false });
                    }
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref STICKYKEYS pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref FILTERKEYS pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref TOGGLEKEYS pvParam, uint fWinIni);

    private const uint SPI_GETSTICKYKEYS = 0x003A;
    private const uint SPI_SETSTICKYKEYS = 0x003B;
    private const uint SPI_GETFILTERKEYS = 0x0032;
    private const uint SPI_SETFILTERKEYS = 0x0033;
    private const uint SPI_GETTOGGLEKEYS = 0x0034;
    private const uint SPI_SETTOGGLEKEYS = 0x0035;
    private const uint SPIF_SENDWININICHANGE = 0x0002;

    private const uint SKF_STICKYKEYSON = 0x00000001;
    private const uint SKF_HOTKEYACTIVE = 0x00000004;
    private const uint SKF_CONFIRMHOTKEY = 0x00000008;
    private const uint SKF_TRIPLECLICK = 0x00000020;
    private const uint SKF_AUDIBLESELECTION = 0x00000100;
    private const uint SKF_REORDERHOTKEYS = 0x00000400;

    private const uint FKF_FILTERKEYSON = 0x00000001;
    private const uint FKF_HOTKEYACTIVE = 0x00000004;
    private const uint FKF_CONFIRMHOTKEY = 0x00000008;
    private const uint FKF_CLICKON = 0x00000080;

    private const uint TKF_TOGGLEKEYSON = 0x00000001;
    private const uint TKF_HOTKEYACTIVE = 0x00000004;
    private const uint TKF_CONFIRMHOTKEY = 0x00000008;

    [StructLayout(LayoutKind.Sequential)]
    private struct STICKYKEYS
    {
        public uint cbSize;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILTERKEYS
    {
        public uint cbSize;
        public uint dwFlags;
        public uint iWaitMSec;
        public uint iDelayMSec;
        public uint iRepeatMSec;
        public uint iBounceMSec;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOGGLEKEYS
    {
        public uint cbSize;
        public uint dwFlags;
    }
}
