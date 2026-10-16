using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace BlenderSceneControls
{
    // Position/foreground only. Keyboard polling remains in the S01 development probe.
    internal static class ScenePointer
    {
        private static readonly int ProcessId = Process.GetCurrentProcess().Id;
        [StructLayout(LayoutKind.Sequential)]
        private struct Point { public int X; public int Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

        internal static bool Supported => Application.platform == RuntimePlatform.WindowsEditor;
        internal static bool IsForeground
        {
            get
            {
                if (!Supported) return false;
                GetWindowThreadProcessId(GetForegroundWindow(), out var process);
                return process == ProcessId;
            }
        }

        internal static Vector2 Read()
        {
            if (!Supported || !GetCursorPos(out var point)) throw new InvalidOperationException("Cannot read cursor.");
            return new Vector2(point.X, point.Y);
        }

        internal static Vector2 Move(Vector2 position)
        {
            if (!Supported || !SetCursorPos(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y)))
                throw new InvalidOperationException("Cannot move cursor.");
            return Read();
        }

        internal static Vector2 Wrap(Vector2 point, Rect bounds, float inset)
        {
            if (bounds.width <= 2 * inset || bounds.height <= 2 * inset)
                throw new ArgumentException("Viewport too small.");
            if (point.x <= bounds.xMin + inset) point.x = bounds.xMax - inset - 1;
            else if (point.x >= bounds.xMax - inset) point.x = bounds.xMin + inset + 1;
            if (point.y <= bounds.yMin + inset) point.y = bounds.yMax - inset - 1;
            else if (point.y >= bounds.yMax - inset) point.y = bounds.yMin + inset + 1;
            return point;
        }
    }
}
