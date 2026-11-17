using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace UpdateDeck.App;

/// <summary>Stores physical screen coordinates and restores inside the nearest current monitor work area.</summary>
internal static class WindowPlacement
{
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
    [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    public static void ConstrainMinimums(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;
        if(handle==IntPtr.Zero){window.MinWidth=Math.Min(900,SystemParameters.WorkArea.Width);window.MinHeight=Math.Min(620,SystemParameters.WorkArea.Height);return;}
        var area=System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(window);
        window.MinWidth=Math.Min(900,area.Width/dpi.DpiScaleX);
        window.MinHeight=Math.Min(620,area.Height/dpi.DpiScaleY);
    }
    public static StoredWindowBounds? Capture(Window window)
    {
        var handle=new WindowInteropHelper(window).Handle;
        return handle!=IntPtr.Zero&&GetWindowRect(handle,out var rect)?new(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top):null;
    }
    public static void Restore(Window window,StoredWindowBounds? bounds)
    {
        if(bounds==null)return;
        var requested=new System.Drawing.Rectangle(bounds.Left,bounds.Top,bounds.Width,bounds.Height);
        var screen=System.Windows.Forms.Screen.FromRectangle(requested);var area=screen.WorkingArea;
        int width=Math.Min(bounds.Width,area.Width),height=Math.Min(bounds.Height,area.Height);
        int left=Math.Clamp(bounds.Left,area.Left,area.Right-width),top=Math.Clamp(bounds.Top,area.Top,area.Bottom-height);
        SetWindowPos(new WindowInteropHelper(window).Handle,IntPtr.Zero,left,top,width,height,0x214);
    }
}
