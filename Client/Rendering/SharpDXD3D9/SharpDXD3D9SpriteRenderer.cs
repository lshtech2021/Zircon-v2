using Vortice.Direct3D9;
using Vortice.D3DCompiler;
using Vortice.Mathematics;
using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Client.Rendering.SharpDXD3D9
{
    public sealed class SharpDXD3D9SpriteRenderer : IDisposable
    {
        private readonly IDirect3DDevice9 _device;

        private IDirect3DVertexShader9 _vertexShader;
        private IDirect3DVertexShader9 _shadowVertexShader;
        private IDirect3DPixelShader9 _outlinePixelShader;
        private IDirect3DPixelShader9 _grayscalePixelShader;
        private IDirect3DPixelShader9 _dropShadowPixelShader;
        private IDirect3DVertexBuffer9 _vertexBuffer;
        private IDirect3DVertexDeclaration9 _vertexDeclaration;

        private const string OutlineShaderFileName = "OutlineD3D9.hlsl";
        private const string GrayscaleShaderFileName = "GrayscaleD3D9.hlsl";
        private const string DropShadowShaderFileName = "DropShadowD3D9.hlsl";

        [StructLayout(LayoutKind.Sequential)]
        private struct VertexType
        {
            public Vector2 Position;
            public Vector2 TexCoord;
            public int Color;  // ARGB format: (A << 24) | (R << 16) | (G << 8) | B
        }

        public bool SupportsOutlineShader => _outlinePixelShader != null && _vertexShader != null;
        public bool SupportsGrayscaleShader => _grayscalePixelShader != null && _vertexShader != null;
        public bool SupportsDropShadowShader => _dropShadowPixelShader != null && _shadowVertexShader != null;

        public SharpDXD3D9SpriteRenderer(IDirect3DDevice9 device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));

            InitializeShaders();
            InitializeBuffers();
        }

        private void InitializeShaders()
        {
            InitializeOutlineShader();
            InitializeGrayscaleShader();
            InitializeDropShadowShader();
        }

        private void InitializeOutlineShader()
        {
            string shaderPath = FindShaderPath(OutlineShaderFileName);

            if (string.IsNullOrEmpty(shaderPath) || !File.Exists(shaderPath))
                return;

            var compiledVertex = Compiler.CompileFromFile(shaderPath, "VS", "vs_3_0", ShaderFlags.OptimizationLevel3);
            var compiledPixel = Compiler.CompileFromFile(shaderPath, "PS_OUTLINE", "ps_3_0", ShaderFlags.OptimizationLevel3);
            
            _vertexShader = _device.CreateVertexShader(compiledVertex.AsBytes());
            _outlinePixelShader = _device.CreatePixelShader(compiledPixel.AsBytes());
            
            compiledVertex.Dispose();
            compiledPixel.Dispose();
        }

        private void InitializeGrayscaleShader()
        {
            string shaderPath = FindShaderPath(GrayscaleShaderFileName);

            if (string.IsNullOrEmpty(shaderPath) || !File.Exists(shaderPath))
                return;

            var compiledPixel = Compiler.CompileFromFile(shaderPath, "PS_GRAY", "ps_3_0", ShaderFlags.OptimizationLevel3);
            
            _grayscalePixelShader = _device.CreatePixelShader(compiledPixel.AsBytes());
            
            compiledPixel.Dispose();
        }

        private void InitializeDropShadowShader()
        {
            string shaderPath = FindShaderPath(DropShadowShaderFileName);

            if (string.IsNullOrEmpty(shaderPath) || !File.Exists(shaderPath))
                return;

            var compiledVertex = Compiler.CompileFromFile(shaderPath, "VS", "vs_3_0", ShaderFlags.OptimizationLevel3);
            var compiledPixel = Compiler.CompileFromFile(shaderPath, "PS_SHADOW", "ps_3_0", ShaderFlags.OptimizationLevel3);
            
            _shadowVertexShader = _device.CreateVertexShader(compiledVertex.AsBytes());
            _dropShadowPixelShader = _device.CreatePixelShader(compiledPixel.AsBytes());
            
            compiledVertex.Dispose();
            compiledPixel.Dispose();
        }

        private static string FindShaderPath(string filename)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;

            string[] candidates = new[]
            {
                Path.Combine(baseDirectory, "Rendering", "SharpDXD3D9", "Shaders", filename)
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private void InitializeBuffers()
        {
            _vertexBuffer = _device.CreateVertexBuffer(Marshal.SizeOf<VertexType>() * 4, Usage.WriteOnly | Usage.Dynamic, VertexFormat.None, Pool.Default);

            _vertexDeclaration = _device.CreateVertexDeclaration(new[]
            {
                new VertexElement(0, 0, DeclarationType.Float2, DeclarationMethod.Default, DeclarationUsage.Position, 0),
                new VertexElement(0, 8, DeclarationType.Float2, DeclarationMethod.Default, DeclarationUsage.TextureCoordinate, 0),
                new VertexElement(0, 16, DeclarationType.Color, DeclarationMethod.Default, DeclarationUsage.Color, 0),
                VertexElement.VertexDeclarationEnd
            });
        }

        public void DrawOutlined(IDirect3DTexture9 texture, System.Drawing.RectangleF destination, System.Drawing.Rectangle? source, System.Drawing.Color color, Matrix3x2 transform, Color4 outlineColor, float outlineThickness)
        {
            if (!SupportsOutlineShader || texture == null || texture.Disposed)
                return;

            float effectiveThickness = outlineThickness > 0 ? 1.0f : 0.0f;

            var stateBlock = _device.CreateStateBlock(StateBlockType.All);
            stateBlock.Capture();

            var desc = texture.GetLevelDescription(0);

            float left = destination.Left;
            float right = destination.Right;
            float top = destination.Top;
            float bottom = destination.Bottom;

            float u1 = 0, v1 = 0, u2 = 1, v2 = 1;
            if (source.HasValue)
            {
                u1 = source.Value.Left / (float)desc.Width;
                v1 = source.Value.Top / (float)desc.Height;
                u2 = source.Value.Right / (float)desc.Width;
                v2 = source.Value.Bottom / (float)desc.Height;
            }

            if (effectiveThickness > 0)
            {
                left -= effectiveThickness;
                right += effectiveThickness;
                top -= effectiveThickness;
                bottom += effectiveThickness;

                float uPad = effectiveThickness / desc.Width;
                float vPad = effectiveThickness / desc.Height;
                u1 -= uPad;
                v1 -= vPad;
                u2 += uPad;
                v2 += vPad;
            }

            int vertexColor = PackColorARGB(color);

            var viewport = _device.Viewport;

            UpdateMatrix(transform, viewport.Width, viewport.Height);
            UpdateOutlineConstants(desc.Width, desc.Height, outlineColor, effectiveThickness, u1, v1, u2, v2);

            IntPtr dataPtr = _vertexBuffer.Lock(0, 0, LockFlags.Discard);
            unsafe
            {
                VertexType* vertices = (VertexType*)dataPtr;
                vertices[0] = new VertexType { Position = new Vector2(left, top), TexCoord = new Vector2(u1, v1), Color = vertexColor };
                vertices[1] = new VertexType { Position = new Vector2(right, top), TexCoord = new Vector2(u2, v1), Color = vertexColor };
                vertices[2] = new VertexType { Position = new Vector2(left, bottom), TexCoord = new Vector2(u1, v2), Color = vertexColor };
                vertices[3] = new VertexType { Position = new Vector2(right, bottom), TexCoord = new Vector2(u2, v2), Color = vertexColor };
            }
            _vertexBuffer.Unlock();

            _device.SetRenderState(RenderState.AlphaBlendEnable, true);
            _device.SetRenderState(RenderState.SourceBlend, (int)Vortice.Direct3D9.Blend.SourceAlpha);
            _device.SetRenderState(RenderState.DestinationBlend, (int)Vortice.Direct3D9.Blend.InverseSourceAlpha);
            _device.SetRenderState(RenderState.CullMode, (int)Cull.None);

            _device.SetTexture(0, texture);
            _device.SetSamplerState(0, SamplerState.AddressU, (int)TextureAddress.Clamp);
            _device.SetSamplerState(0, SamplerState.AddressV, (int)TextureAddress.Clamp);
            _device.SetSamplerState(0, SamplerState.MinFilter, (int)TextureFilter.Point);
            _device.SetSamplerState(0, SamplerState.MagFilter, (int)TextureFilter.Point);
            _device.SetSamplerState(0, SamplerState.MipFilter, (int)TextureFilter.Point);

            _device.VertexDeclaration = _vertexDeclaration;
            _device.SetStreamSource(0, _vertexBuffer, 0, Marshal.SizeOf<VertexType>());
            _device.VertexShader = _vertexShader;
            _device.PixelShader = _outlinePixelShader;

            _device.DrawPrimitives(PrimitiveType.TriangleStrip, 0, 2);

            stateBlock.Apply();
            stateBlock.Dispose();
        }

        public void DrawGrayscale(IDirect3DTexture9 texture, System.Drawing.RectangleF destination, System.Drawing.Rectangle? source, System.Drawing.Color color, Matrix3x2 transform)
        {
            if (!SupportsGrayscaleShader || texture == null || texture.Disposed)
                return;

            var stateBlock = _device.CreateStateBlock(StateBlockType.All);
            stateBlock.Capture();

            var desc = texture.GetLevelDescription(0);

            float left = destination.Left;
            float right = destination.Right;
            float top = destination.Top;
            float bottom = destination.Bottom;

            float u1 = 0, v1 = 0, u2 = 1, v2 = 1;
            if (source.HasValue)
            {
                u1 = source.Value.Left / (float)desc.Width;
                v1 = source.Value.Top / (float)desc.Height;
                u2 = source.Value.Right / (float)desc.Width;
                v2 = source.Value.Bottom / (float)desc.Height;
            }

            int vertexColor = PackColorARGB(color);

            var viewport = _device.Viewport;

            UpdateMatrix(transform, viewport.Width, viewport.Height);

            IntPtr dataPtr = _vertexBuffer.Lock(0, 0, LockFlags.Discard);
            unsafe
            {
                VertexType* vertices = (VertexType*)dataPtr;
                vertices[0] = new VertexType { Position = new Vector2(left, top), TexCoord = new Vector2(u1, v1), Color = vertexColor };
                vertices[1] = new VertexType { Position = new Vector2(right, top), TexCoord = new Vector2(u2, v1), Color = vertexColor };
                vertices[2] = new VertexType { Position = new Vector2(left, bottom), TexCoord = new Vector2(u1, v2), Color = vertexColor };
                vertices[3] = new VertexType { Position = new Vector2(right, bottom), TexCoord = new Vector2(u2, v2), Color = vertexColor };
            }
            _vertexBuffer.Unlock();

            _device.SetRenderState(RenderState.AlphaBlendEnable, SharpDXD3D9Manager.Blending);
            _device.SetRenderState(RenderState.SourceBlend, (int)Vortice.Direct3D9.Blend.InverseDestinationColor);
            _device.SetRenderState(RenderState.DestinationBlend, (int)Vortice.Direct3D9.Blend.One);
            _device.SetRenderState(RenderState.CullMode, (int)Cull.None);

            _device.SetTexture(0, texture);
            _device.SetSamplerState(0, SamplerState.AddressU, (int)TextureAddress.Clamp);
            _device.SetSamplerState(0, SamplerState.AddressV, (int)TextureAddress.Clamp);
            _device.SetSamplerState(0, SamplerState.MinFilter, (int)TextureFilter.Point);
            _device.SetSamplerState(0, SamplerState.MagFilter, (int)TextureFilter.Point);
            _device.SetSamplerState(0, SamplerState.MipFilter, (int)TextureFilter.Point);

            _device.VertexDeclaration = _vertexDeclaration;
            _device.SetStreamSource(0, _vertexBuffer, 0, Marshal.SizeOf<VertexType>());
            _device.VertexShader = _vertexShader;
            _device.PixelShader = _grayscalePixelShader;

            _device.DrawPrimitives(PrimitiveType.TriangleStrip, 0, 2);

            stateBlock.Apply();
            stateBlock.Dispose();
        }

        public void DrawDropShadow(IDirect3DTexture9 texture, System.Drawing.RectangleF destination, System.Drawing.RectangleF shadowBounds, System.Drawing.Rectangle? source, System.Drawing.Color color, Matrix3x2 transform, Color4 shadowColor, float shadowWidth, float shadowMaxOpacity)
        {
            if (!SupportsDropShadowShader || texture == null || texture.Disposed)
                return;

            var stateBlock = _device.CreateStateBlock(StateBlockType.All);
            stateBlock.Capture();

            var desc = texture.GetLevelDescription(0);

            float imageLeft = shadowBounds.Left;
            float imageRight = shadowBounds.Right;
            float imageTop = shadowBounds.Top;
            float imageBottom = shadowBounds.Bottom;

            float left = destination.Left;
            float right = destination.Right;
            float top = destination.Top;
            float bottom = destination.Bottom;

            float u1 = 0, v1 = 0, u2 = 1, v2 = 1;
            if (source.HasValue)
            {
                u1 = source.Value.Left / (float)desc.Width;
                v1 = source.Value.Top / (float)desc.Height;
                u2 = source.Value.Right / (float)desc.Width;
                v2 = source.Value.Bottom / (float)desc.Height;
            }

            float effectiveWidth = Math.Max(0f, shadowWidth);

            if (effectiveWidth > 0)
            {
                left -= effectiveWidth;
                right += effectiveWidth;
                top -= effectiveWidth;
                bottom += effectiveWidth;

                float uPad = effectiveWidth / desc.Width;
                float vPad = effectiveWidth / desc.Height;
                u1 -= uPad;
                v1 -= vPad;
                u2 += uPad;
                v2 += vPad;
            }

            int vertexColor = PackColorARGB(color);

            var viewport = _device.Viewport;

            UpdateMatrix(transform, viewport.Width, viewport.Height);
            UpdateShadowConstants(imageLeft, imageTop, imageRight, imageBottom, effectiveWidth, shadowMaxOpacity);

            IntPtr dataPtr = _vertexBuffer.Lock(0, 0, LockFlags.Discard);
            unsafe
            {
                VertexType* vertices = (VertexType*)dataPtr;
                vertices[0] = new VertexType { Position = new Vector2(left, top), TexCoord = new Vector2(u1, v1), Color = vertexColor };
                vertices[1] = new VertexType { Position = new Vector2(right, top), TexCoord = new Vector2(u2, v1), Color = vertexColor };
                vertices[2] = new VertexType { Position = new Vector2(left, bottom), TexCoord = new Vector2(u1, v2), Color = vertexColor };
                vertices[3] = new VertexType { Position = new Vector2(right, bottom), TexCoord = new Vector2(u2, v2), Color = vertexColor };
            }
            _vertexBuffer.Unlock();

            _device.SetRenderState(RenderState.AlphaBlendEnable, true);
            _device.SetRenderState(RenderState.SourceBlend, (int)Vortice.Direct3D9.Blend.SourceAlpha);
            _device.SetRenderState(RenderState.DestinationBlend, (int)Vortice.Direct3D9.Blend.InverseSourceAlpha);
            _device.SetRenderState(RenderState.CullMode, (int)Cull.None);

            _device.SetTexture(0, texture);
            _device.SetSamplerState(0, SamplerState.AddressU, (int)TextureAddress.Clamp);
            _device.SetSamplerState(0, SamplerState.AddressV, (int)TextureAddress.Clamp);
            _device.SetSamplerState(0, SamplerState.MinFilter, (int)TextureFilter.Point);
            _device.SetSamplerState(0, SamplerState.MagFilter, (int)TextureFilter.Point);
            _device.SetSamplerState(0, SamplerState.MipFilter, (int)TextureFilter.Point);

            _device.VertexDeclaration = _vertexDeclaration;
            _device.SetStreamSource(0, _vertexBuffer, 0, Marshal.SizeOf<VertexType>());
            _device.VertexShader = _shadowVertexShader;
            _device.PixelShader = _dropShadowPixelShader;

            _device.DrawPrimitives(PrimitiveType.TriangleStrip, 0, 2);

            stateBlock.Apply();
            stateBlock.Dispose();
        }

        private void UpdateMatrix(Matrix3x2 transform, int backBufferWidth, int backBufferHeight)
        {
            Matrix4x4 projection = Matrix4x4.Identity;
            projection.M11 = 2f / backBufferWidth;
            projection.M22 = -2f / backBufferHeight;

            // Apply a half-pixel offset to align texels to pixel centers when using point sampling in D3D9.
            float halfPixelX = 1f / backBufferWidth;
            float halfPixelY = 1f / backBufferHeight;
            projection.M41 = -1f - halfPixelX;
            projection.M42 = 1f + halfPixelY;

            Matrix4x4 world = Matrix4x4.Identity;
            world.M11 = transform.M11;
            world.M12 = transform.M12;
            world.M21 = transform.M21;
            world.M22 = transform.M22;
            world.M41 = transform.M31;
            world.M42 = transform.M32;

            Matrix4x4 final = Matrix4x4.Multiply(world, projection);
            final = Matrix4x4.Transpose(final);

            unsafe
            {
                float* matrixData = stackalloc float[16];
                matrixData[0] = final.M11; matrixData[1] = final.M12; matrixData[2] = final.M13; matrixData[3] = final.M14;
                matrixData[4] = final.M21; matrixData[5] = final.M22; matrixData[6] = final.M23; matrixData[7] = final.M24;
                matrixData[8] = final.M31; matrixData[9] = final.M32; matrixData[10] = final.M33; matrixData[11] = final.M34;
                matrixData[12] = final.M41; matrixData[13] = final.M42; matrixData[14] = final.M43; matrixData[15] = final.M44;
                
                _device.SetVertexShaderConstantF(0, new IntPtr(matrixData), 4);
            }
        }

        private void UpdateOutlineConstants(int texWidth, int texHeight, Color4 outlineColor, float outlineThickness, float u1, float v1, float u2, float v2)
        {
            unsafe
            {
                float* colorData = stackalloc float[4];
                colorData[0] = outlineColor.R;
                colorData[1] = outlineColor.G;
                colorData[2] = outlineColor.B;
                colorData[3] = outlineColor.A;
                _device.SetPixelShaderConstantF(4, new IntPtr(colorData), 1);

                float* texInfoData = stackalloc float[4];
                texInfoData[0] = texWidth;
                texInfoData[1] = texHeight;
                texInfoData[2] = outlineThickness;
                texInfoData[3] = 0f;
                _device.SetPixelShaderConstantF(5, new IntPtr(texInfoData), 1);

                float* uvData = stackalloc float[4];
                uvData[0] = u1;
                uvData[1] = v1;
                uvData[2] = u2;
                uvData[3] = v2;
                _device.SetPixelShaderConstantF(6, new IntPtr(uvData), 1);
            }
        }

        private void UpdateShadowConstants(float imageLeft, float imageTop, float imageRight, float imageBottom, float shadowWidth, float shadowMaxOpacity)
        {
            unsafe
            {
                float* boundsData = stackalloc float[4];
                boundsData[0] = imageLeft;
                boundsData[1] = imageTop;
                boundsData[2] = imageRight;
                boundsData[3] = imageBottom;
                _device.SetPixelShaderConstantF(4, new IntPtr(boundsData), 1);

                float* shadowData = stackalloc float[4];
                shadowData[0] = shadowWidth;
                shadowData[1] = shadowMaxOpacity;
                shadowData[2] = 0f;
                shadowData[3] = 0f;
                _device.SetPixelShaderConstantF(5, new IntPtr(shadowData), 1);
            }
        }

        // Helper method to pack System.Drawing.Color to ARGB integer format for D3D9
        private static int PackColorARGB(System.Drawing.Color color)
        {
            return (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;
        }

        public void Dispose()
        {
            _vertexBuffer?.Dispose();
            _vertexDeclaration?.Dispose();
            _vertexShader?.Dispose();
            _outlinePixelShader?.Dispose();
            _grayscalePixelShader?.Dispose();
            _dropShadowPixelShader?.Dispose();
            _shadowVertexShader?.Dispose();
        }
    }
}
