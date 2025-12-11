using Vortice.Direct3D9;
using Vortice.Mathematics;
using System;
using System.Drawing;
using System.Linq;
using System.Numerics;

namespace Client.Extensions;

public static class SharpDXExtensions
{
    public static void Draw(this Sprite sprite, IDirect3DTexture9 texture, Rectangle? sourceRectangle, Vector3? center, Vector3? position, Color color)
    {
        sprite.DrawInternal(texture, sourceRectangle, center, position, color.ToColorBGRA());
    }

    public static void Draw(this Sprite sprite, IDirect3DTexture9 texture, Rectangle? sourceRectangle, Vector3? center, Vector3? position, Color4 color)
    {
        sprite.DrawInternal(texture, sourceRectangle, center, position, color.ToColorBGRA());
    }

    public static void Draw(this Sprite sprite, IDirect3DTexture9 texture, Vector3? center, Vector3? position, Color color)
    {
        sprite.Draw(texture, null, center, position, color);
    }

    public static void Draw(this Sprite sprite, IDirect3DTexture9 texture, Vector3? center, Vector3? position, Color4 color)
    {
        sprite.Draw(texture, null, center, position, color);
    }

    public static void Draw(this Sprite sprite, IDirect3DTexture9 texture, Color color)
    {
        sprite.Draw(texture, null, null, null, color);
    }

    public static void Draw(this Sprite sprite, IDirect3DTexture9 texture, Color4 color)
    {
        sprite.Draw(texture, null, null, null, color);
    }

    public static void Draw(this Line line, Vector2[] vertexList, Color color)
    {
        line.DrawInternal(vertexList, color.ToColorBGRA());
    }

    public static void Draw(this Line line, Vector2[] vertexList, Color4 color)
    {
        line.DrawInternal(vertexList, color.ToColorBGRA());
    }

    public static void Clear(this IDirect3DDevice9 device, ClearFlags flags, Color color, float z, int stencil)
    {
        ArgumentNullException.ThrowIfNull(device);

        device.Clear(0, null, flags, color.ToColorBGRA().Value, z, stencil);
    }

    public static void Clear(this IDirect3DDevice9 device, ClearFlags flags, int color, float z, int stencil)
    {
        ArgumentNullException.ThrowIfNull(device);

        device.Clear(0, null, flags, Color.FromArgb(color).ToColorBGRA().Value, z, stencil);
    }

    public static void Clear(this IDirect3DDevice9 device, ClearFlags flags, Color color, float z, int stencil, Rectangle[] rectangles)
    {
        ArgumentNullException.ThrowIfNull(device);

        Rect[] rects = rectangles?.Select(rectangle => new Rect(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom)).ToArray();

        device.Clear(rects?.Length ?? 0, rects, flags, color.ToColorBGRA().Value, z, stencil);
    }

    private static void DrawInternal(this Sprite sprite, IDirect3DTexture9 texture, Rectangle? sourceRectangle, Vector3? center, Vector3? position, ColorBgra color)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        ArgumentNullException.ThrowIfNull(texture);

        Rect? rect = sourceRectangle.HasValue ? new Rect(sourceRectangle.Value.Left, sourceRectangle.Value.Top, sourceRectangle.Value.Right, sourceRectangle.Value.Bottom) : null;

        sprite.Draw(texture, color, rect, center, position);
    }

    private static void DrawInternal(this Line line, Vector2[] vertexList, ColorBgra color)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(vertexList);

        line.Draw(vertexList, color);
    }
}

public static class SharpDXColorExtensions
{
    public static Color4 ToColor4(this Color color)
    {
        return new Color4(
            color.R / 255f,
            color.G / 255f,
            color.B / 255f,
            color.A / 255f);
    }

    public static Color ToColor(this Color4 color)
    {
        return Color.FromArgb(
            ToByte(color.A),
            ToByte(color.R),
            ToByte(color.G),
            ToByte(color.B));
    }

    public static ColorBgra ToColorBGRA(this Color color)
    {
        return new ColorBgra(color.B, color.G, color.R, color.A);
    }

    public static ColorBgra ToColorBGRA(this Color4 color)
    {
        return new ColorBgra(
            ToByte(color.B),
            ToByte(color.G),
            ToByte(color.R),
            ToByte(color.A));
    }

    private static byte ToByte(float value)
    {
        if (value <= 0f) return 0;
        if (value >= 1f) return 255;

        return (byte)Math.Round(value * 255f);
    }
}
