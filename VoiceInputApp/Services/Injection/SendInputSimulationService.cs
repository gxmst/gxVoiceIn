using System.Runtime.InteropServices;

namespace VoiceInputApp.Services.Injection;

public class SendInputSimulationService : IInputSimulationService
{
    private const ushort VkControl = 0x11;
    private const ushort VkLControl = 0xA2;
    private const ushort VkRControl = 0xA3;
    private const ushort VkShift = 0x10;
    private const ushort VkLShift = 0xA0;
    private const ushort VkRShift = 0xA1;
    private const ushort VkMenu = 0x12;
    private const ushort VkLMenu = 0xA4;
    private const ushort VkRMenu = 0xA5;
    private const ushort VkLWin = 0x5B;
    private const ushort VkRWin = 0x5C;
    private const ushort VkV = 0x56;
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfKeydown = 0x0000;

    public Task SendPasteShortcutAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var inputs = new INPUT[]
        {
            CreateKeyboardInput(VkControl, 0),
            CreateKeyboardInput(VkV, 0),
            CreateKeyboardInput(VkV, KeyeventfKeyup),
            CreateKeyboardInput(VkControl, KeyeventfKeyup)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            var win32Error = Marshal.GetLastWin32Error();
            FallbackWithKeybdEvent();

            if (win32Error != 0)
            {
                return Task.CompletedTask;
            }

            throw new InvalidOperationException($"SendInput failed, sent {sent}/{inputs.Length} events.");
        }

        return Task.CompletedTask;
    }

    public Task ReleaseModifierKeysAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var modifierKeys = new[]
        {
            VkControl,
            VkLControl,
            VkRControl,
        };

        var inputs = modifierKeys
            .Select(vk => CreateKeyboardInput(vk, KeyeventfKeyup))
            .ToArray();

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            foreach (var vk in modifierKeys)
            {
                keybd_event(vk, 0, KeyeventfKeyup, 0);
            }
        }

        return Task.CompletedTask;
    }

    private static void FallbackWithKeybdEvent()
    {
        keybd_event(VkControl, 0, KeyeventfKeydown, 0);
        keybd_event(VkV, 0, KeyeventfKeydown, 0);
        keybd_event(VkV, 0, KeyeventfKeyup, 0);
        keybd_event(VkControl, 0, KeyeventfKeyup, 0);
    }

    private static INPUT CreateKeyboardInput(ushort vk, uint flags)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = flags
                }
            }
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(ushort bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
