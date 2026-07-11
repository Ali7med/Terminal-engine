namespace Terminal.Core.Screen;

/// <summary>Mouse buttons as encoded in VT mouse reports (the low bits of Cb).</summary>
public enum MouseButton { Left = 0, Middle = 1, Right = 2, WheelUp = 64, WheelDown = 65 }

/// <summary>Kind of mouse event to report to the application.</summary>
public enum MouseEventType { Press, Release, Move }
