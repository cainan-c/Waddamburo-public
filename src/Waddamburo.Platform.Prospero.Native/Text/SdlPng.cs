using SharpProspero.Graphics;
using SharpProspero.Modules;
using SharpProspero.Interop.Sysmodule;

namespace Waddamburo.Platform.Sdl.Text;

public static unsafe class SdlPng
{
    private static readonly object Gate = new();
    private static bool _loaded;
    public static RgbaTextSurface? Decode(ReadOnlySpan<byte> png)
    {
        lock (Gate)
        {
            try
            {
                if (!_loaded) { SystemModule.Load(SystemModuleId.PngDec); _loaded = true; }
                using var image = PngImage.Decode(png);
                var source = image.AsSurface();
                var pixels = new byte[checked(image.Width * image.Height * 4)];
                for (int i = 0; i < image.Width * image.Height; i++)
                {
                    uint color = source.Pixels[i];
                    pixels[i*4] = (byte)(color >> 16); pixels[i*4+1] = (byte)(color >> 8);
                    pixels[i*4+2] = (byte)color; pixels[i*4+3] = (byte)(color >> 24);
                }
                return new((uint)image.Width, (uint)image.Height, pixels);
            }
            catch (Exception error) { Console.Error.WriteLine("PS5 PNG decode failed: " + error.Message); return null; }
        }
    }
}
