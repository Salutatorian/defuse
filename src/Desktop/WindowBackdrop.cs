using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Defuse.Desktop;

public static class WindowBackdrop
{
    public enum Kind
    {
        Solid,
        Mica,
        Acrylic
    }

    public static Kind Apply(Window window, bool opaqueVideo)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return Kind.Solid;

        DesktopAcrylic.Hide();
        var dark = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        var round = 2;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));

        var hostOff = 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseHostBackdropBrush, ref hostOff, sizeof(int));
        SetAccent(hwnd, AccentDisabled, 0);
        var blurOff = new BlurBehind { Flags = 1, Enable = false };
        _ = DwmEnableBlurBehindWindow(hwnd, ref blurOff);
        var alphaOff = 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaRedirectionBitmapAlpha, ref alphaOff, sizeof(int));

        if (opaqueVideo)
        {
            var none = DwmsbtNone;
            _ = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref none, sizeof(int));
            var videoMargins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            _ = DwmExtendFrameIntoClientArea(hwnd, ref videoMargins);
            var captionBlack = 0;
            _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionBlack, sizeof(int));
            if (PresentationSource.FromVisual(window) is HwndSource videoSource)
                videoSource.CompositionTarget.BackgroundColor = Colors.Black;
            return Kind.Solid;
        }

        if (PresentationSource.FromVisual(window) is HwndSource source)
            source.CompositionTarget.BackgroundColor = Colors.Transparent;

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var captionNone = unchecked((int)0xFFFFFFFE);
        _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionNone, sizeof(int));

        var acrylic = DwmsbtTransientWindow;
        if (DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref acrylic, sizeof(int)) == 0)
            return Kind.Acrylic;

        var mica = DwmsbtMainWindow;
        if (DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref mica, sizeof(int)) == 0)
            return Kind.Mica;

        return Kind.Solid;
    }

    public static void SetSquare(Window window, bool square)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var preference = square ? 1 : 2;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    public static void HideBorder(Window window, bool hide)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var color = hide ? unchecked((int)0xFFFFFFFE) : unchecked((int)0xFFFFFFFF);
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref color, sizeof(int));
    }

    private static void SetAccent(IntPtr hwnd, int state, uint gradientColor)
    {
        var policy = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = state == AccentAcrylicBlur ? 2 : 0,
            GradientColor = gradientColor
        };
        var size = Marshal.SizeOf<AccentPolicy>();
        var data = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, data, false);
            var attribute = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = data,
                SizeOfData = size
            };
            _ = SetWindowCompositionAttribute(hwnd, ref attribute);
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    private const int DwmwaUseHostBackdropBrush = 17;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaRedirectionBitmapAlpha = 39;
    private const int DwmsbtNone = 1;
    private const int DwmsbtMainWindow = 2;
    private const int DwmsbtTransientWindow = 3;
    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentAcrylicBlur = 4;
    private const int AccentHostBackdrop = 5;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref BlurBehind blur);

    [StructLayout(LayoutKind.Sequential)]
    private struct BlurBehind
    {
        public int Flags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Enable;
        public IntPtr Region;
        [MarshalAs(UnmanagedType.Bool)]
        public bool Transition;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }
}
