using System.Runtime.InteropServices;

namespace Broadme.Win.Services.Input;

public sealed class InputSimulator
{
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x01000;

    public void MoveMouse(double x, double y)
    {
        SetCursorPos((int)Math.Round(x), (int)Math.Round(y));
    }

    public void LeftClick(double x, double y)
    {
        MoveMouse(x, y);
        var inputs = new[]
        {
            new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } } },
            new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } } }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public void DoubleClick(double x, double y)
    {
        LeftClick(x, y);
        Thread.Sleep(50);
        LeftClick(x, y);
    }

    public void Scroll(int deltaX, int deltaY)
    {
        var list = new List<INPUT>();
        if (deltaY != 0)
        {
            list.Add(new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_WHEEL, mouseData = (uint)(deltaY * 120) } } });
        }
        if (deltaX != 0)
        {
            list.Add(new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_HWHEEL, mouseData = (uint)(deltaX * 120) } } });
        }
        if (list.Count > 0)
        {
            SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf<INPUT>());
        }
    }
}
