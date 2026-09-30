using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace Defuse.Desktop;

static class DesktopAcrylic
{
    private static object? _queue;
    private static Compositor? _compositor;
    private static DesktopWindowTarget? _target;
    private static ContainerVisual? _root;
    private static SpriteVisual? _frost;
    private static SpriteVisual? _tint;

    public static bool TryApply(Window window, IntPtr hwnd)
    {
        try
        {
            EnsureQueue();
            _compositor ??= new Compositor();
            if (_root == null)
                Build(window, hwnd);
            _root!.IsVisible = true;
            Resize(hwnd);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Hide()
    {
        if (_root != null)
            _root.IsVisible = false;
    }

    private static void Build(Window window, IntPtr hwnd)
    {
        var compositor = _compositor!;
        var interop = compositor.As<ICompositorDesktopInterop>();
        interop.CreateDesktopWindowTarget(hwnd, false, out var raw);
        _target = DesktopWindowTarget.FromAbi(raw);
        _root = compositor.CreateContainerVisual();
        _frost = compositor.CreateSpriteVisual();
        _frost.Brush = compositor.CreateHostBackdropBrush();
        _tint = compositor.CreateSpriteVisual();
        _tint.Brush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0xA0, 0x10, 0x18, 0x28));
        _root.Children.InsertAtBottom(_frost);
        _root.Children.InsertAtTop(_tint);
        _target.Root = _root;
        window.SizeChanged += (_, _) => Resize(new WindowInteropHelper(window).Handle);
    }

    private static void Resize(IntPtr hwnd)
    {
        if (_root == null || _frost == null || _tint == null || hwnd == IntPtr.Zero)
            return;
        GetClientRect(hwnd, out var rect);
        var size = new Vector2(Math.Max(rect.Right, 1), Math.Max(rect.Bottom, 1));
        _root.Size = size;
        _frost.Size = size;
        _tint.Size = size;
    }

    private static void EnsureQueue()
    {
        if (_queue != null)
            return;
        var options = new DispatcherQueueOptions
        {
            dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
            threadType = 2,
            apartmentType = 2
        };
        _ = CreateDispatcherQueueController(options, out _queue);
    }

    [DllImport("coremessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, [MarshalAs(UnmanagedType.IUnknown)] out object? controller);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RectNative rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;
        public int apartmentType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [ComImport]
    [Guid("29E691FA-4567-4DCA-B319-D0F207EB6807")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(IntPtr hwndTarget, bool isTopmost, out IntPtr result);
    }
}
