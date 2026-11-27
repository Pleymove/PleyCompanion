using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace OpenClicky.Windows.Launcher;

internal static class ShellIcon
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Size { public int Width, Height; }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageFactory
    {
        [PreserveSig] int GetImage(Size size, uint flags, out nint bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint binding, ref Guid iid, out IImageFactory item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint bitmap);

    public static BitmapSource? Load(string path)
    {
        var iid = typeof(IImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, 0, ref iid, out var factory) < 0) return null;
        try
        {
            if (factory.GetImage(new Size { Width = 48, Height = 48 }, 4, out var handle) < 0) return null;
            try
            {
                var image = Imaging.CreateBitmapSourceFromHBitmap(handle, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                return image;
            }
            finally { DeleteObject(handle); }
        }
        finally { Marshal.FinalReleaseComObject(factory); }
    }
}
