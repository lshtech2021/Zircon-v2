using SharpDX.Direct3D9;
using SharpDX.Mathematics.Interop;
using System;
using System.Drawing;
using System.Linq;
using System.Numerics;
using Color4 = SharpDX.Color4;
using VorticeD3D9Device = Vortice.Direct3D9.IDirect3DDevice9;
using VorticeD3D9Sprite = Vortice.Direct3D9.ID3DXSprite;
using VorticeD3D9Line = Vortice.Direct3D9.ID3DXLine;
using VorticeD3D9Texture = Vortice.Direct3D9.IDirect3DTexture9;
using VorticeClearFlags = Vortice.Direct3D9.ClearFlags;
using VorticeColor = Vortice.Mathematics.Color;
using VorticeRect = Vortice.Direct3D9.Rect;

namespace Client.Extensions;

public static class SharpDXExtensions
{
    public static void Draw(this Sprite sprite, Texture texture, Rectangle? sourceRectangle, Vector3? center, Vector3? position, Color color)
    {
        sprite.DrawInternal(texture, sourceRectangle, center, position, color.ToColorBGRA());
    }

    public static void Draw(this Sprite sprite, Texture texture, Rectangle? sourceRectangle, Vector3? center, Vector3? position, Color4 color)
    {
        sprite.DrawInternal(texture, sourceRectangle, center, position, color.ToColorBGRA());
    }

    public static void Draw(this Sprite sprite, Texture texture, Vector3? center, Vector3? position, Color color)
    {
        sprite.Draw(texture, null, center, position, color);
    }

    public static void Draw(this Sprite sprite, Texture texture, Vector3? center, Vector3? position, Color4 color)
    {
        sprite.Draw(texture, null, center, position, color);
    }

    public static void Draw(this Sprite sprite, Texture texture, Color color)
    {
        sprite.Draw(texture, null, null, null, color);
    }

    public static void Draw(this Sprite sprite, Texture texture, Color4 color)
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

    public static void Clear(this Device device, ClearFlags flags, Color color, float z, int stencil)
    {
        ArgumentNullException.ThrowIfNull(device);

        device.Clear(flags, color.ToColorBGRA(), z, stencil);
    }

    public static void Clear(this Device device, ClearFlags flags, int color, float z, int stencil)
    {
        ArgumentNullException.ThrowIfNull(device);

        device.Clear(flags, Color.FromArgb(color).ToColorBGRA(), z, stencil);
    }

    public static void Clear(this Device device, ClearFlags flags, Color color, float z, int stencil, Rectangle[] rectangles)
    {
        ArgumentNullException.ThrowIfNull(device);

        RawRectangle[] rawRectangles = rectangles?.Select(rectangle => new RawRectangle(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom)).ToArray();

        device.Clear(flags, color.ToColorBGRA(), z, stencil, rawRectangles);
    }

    private static void DrawInternal(this Sprite sprite, Texture texture, Rectangle? sourceRectangle, Vector3? center, Vector3? position, RawColorBGRA color)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        ArgumentNullException.ThrowIfNull(texture);

        RawRectangle? rawRectangle = sourceRectangle.HasValue ? ToSharpDXRectangle(sourceRectangle.Value) : null;
        RawVector3? rawCenter = center.HasValue ? ToSharpDXVector3(center.Value) : null;
        RawVector3? rawPosition = position.HasValue ? ToSharpDXVector3(position.Value) : null;

        sprite.Draw(texture, color, rawRectangle, rawCenter, rawPosition);
    }

    private static void DrawInternal(this Line line, Vector2[] vertexList, RawColorBGRA color)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(vertexList);

        RawVector2[] raw = new RawVector2[vertexList.Length];
        for (int i = 0; i < vertexList.Length; i++)
        {
            raw[i] = new RawVector2(vertexList[i].X, vertexList[i].Y);
        }

        line.Draw(raw, color);
    }

    private static RawRectangle ToSharpDXRectangle(Rectangle rectangle) => new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);

    private static RawVector3 ToSharpDXVector3(Vector3 vector) => new(vector.X, vector.Y, vector.Z);
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
            ToByte(color.Alpha),
            ToByte(color.Red),
            ToByte(color.Green),
            ToByte(color.Blue));
    }

    public static RawColorBGRA ToColorBGRA(this Color color)
    {
        return new RawColorBGRA(color.B, color.G, color.R, color.A);
    }

    public static RawColorBGRA ToColorBGRA(this Color4 color)
    {
        return new RawColorBGRA(
            ToByte(color.Blue),
            ToByte(color.Green),
            ToByte(color.Red),
            ToByte(color.Alpha));
    }

    private static byte ToByte(float value)
    {
        if (value <= 0f) return 0;
        if (value >= 1f) return 255;

        return (byte)Math.Round(value * 255f);
    }
}

// Vortice.Direct3D9 Extensions for convenience
public static class VorticeD3D9Extensions
{
    // Extension for drawing with System.Drawing.Color
    public static void Draw(this VorticeD3D9Sprite sprite, VorticeD3D9Texture texture, Rectangle? sourceRectangle, Vector3 center, Vector3 position, Color color)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        ArgumentNullException.ThrowIfNull(texture);

        VorticeRect? rect = sourceRectangle.HasValue ? new VorticeRect(sourceRectangle.Value.Left, sourceRectangle.Value.Top, sourceRectangle.Value.Right, sourceRectangle.Value.Bottom) : null;
        VorticeColor vorticeColor = new VorticeColor(color.R, color.G, color.B, color.A);
        
        Vortice.Direct3D9.D3DX9.Draw(sprite, texture, rect, center, position, vorticeColor);
    }

    // Extension for line drawing with System.Drawing.Color
    public static void Draw(this VorticeD3D9Line line, Vector2[] vertexList, Color color)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(vertexList);

        VorticeColor vorticeColor = new VorticeColor(color.R, color.G, color.B, color.A);
        Vortice.Direct3D9.D3DX9.Draw(line, vertexList, vorticeColor);
    }

    // Extension for clearing with System.Drawing.Color
    public static void Clear(this VorticeD3D9Device device, VorticeClearFlags flags, Color color, float z, int stencil)
    {
        ArgumentNullException.ThrowIfNull(device);

        VorticeColor vorticeColor = new VorticeColor(color.R, color.G, color.B, color.A);
        device.Clear(flags, vorticeColor, z, stencil);
    }

    public static void Clear(this VorticeD3D9Device device, VorticeClearFlags flags, int colorArgb, float z, int stencil)
    {
        ArgumentNullException.ThrowIfNull(device);

        Color color = Color.FromArgb(colorArgb);
        VorticeColor vorticeColor = new VorticeColor(color.R, color.G, color.B, color.A);
        device.Clear(flags, vorticeColor, z, stencil);
    }

    public static void Clear(this VorticeD3D9Device device, VorticeClearFlags flags, Color color, float z, int stencil, Rectangle[] rectangles)
    {
        ArgumentNullException.ThrowIfNull(device);

        VorticeRect[] vorticeRects = rectangles?.Select(rectangle => new VorticeRect(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom)).ToArray();
        VorticeColor vorticeColor = new VorticeColor(color.R, color.G, color.B, color.A);

        device.Clear(flags, vorticeColor, z, stencil, vorticeRects);
    }
}
