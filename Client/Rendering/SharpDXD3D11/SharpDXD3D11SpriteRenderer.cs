using Vortice;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Color = System.Drawing.Color;
using Matrix3x2 = System.Numerics.Matrix3x2;
using Matrix4x4 = System.Numerics.Matrix4x4;
using RectangleF = System.Drawing.RectangleF;
using Vector2 = System.Numerics.Vector2;

namespace Client.Rendering.SharpDXD3D11
{
    public sealed class SharpDXD3D11SpriteRenderer : IDisposable
    {
        private readonly ID3D11Device _device;
        private readonly ID3D11DeviceContext _context;
        private ID3D11VertexShader _vertexShader;
        private ID3D11PixelShader _pixelShader;
        private ID3D11PixelShader _grayscalePixelShader;
        private ID3D11PixelShader _outlinePixelShader;
        private ID3D11PixelShader _dropShadowPixelShader;
        private ID3D11InputLayout _inputLayout;
        private ID3D11Buffer _vertexBuffer;
        private ID3D11Buffer _matrixBuffer;
        private ID3D11Buffer _outlineBuffer;
        private ID3D11Buffer _dropShadowBuffer;
        private ID3D11SamplerState _samplerState;

        private readonly Dictionary<BlendMode, ID3D11BlendState> _blendStates = new Dictionary<BlendMode, ID3D11BlendState>();
        private readonly Dictionary<ID3D11Texture2D, ID3D11ShaderResourceView> _srvCache = new Dictionary<ID3D11Texture2D, ID3D11ShaderResourceView>();

        private const string ShaderFileName = "SpriteD3D11.hlsl";
        private const string OutlineShaderFileName = "OutlineD3D11.hlsl";
        private const string GrayscaleFileName = "GrayscaleD3D11.hlsl";
        private const string DropShadowFileName = "DropShadowD3D11.hlsl";

        [StructLayout(LayoutKind.Sequential)]
        private struct VertexType
        {
            public Vector2 position;
            public Vector2 texture;
            public Color4 color;

            public VertexType(Vector2 pos, Vector2 tex, Color4 col)
            {
                position = pos;
                texture = tex;
                color = col;
            }
        }

        private readonly struct SpriteEffect
        {
            public ID3D11PixelShader Shader { get; }
            public ID3D11Buffer? ConstantBuffer { get; }
            public int ConstantBufferSizeInBytes { get; }
            public float GeometryExpand { get; }
            public bool ExpandUvs { get; }
            public Action<IntPtr>? WriteConstants { get; }

            public bool IsValid => Shader != null;

            public SpriteEffect(ID3D11PixelShader shader, ID3D11Buffer? constantBuffer, int constantBufferSizeInBytes, float geometryExpand, bool expandUvs, Action<IntPtr>? writeConstants)
            {
                Shader = shader;
                ConstantBuffer = constantBuffer;
                ConstantBufferSizeInBytes = constantBufferSizeInBytes;
                GeometryExpand = geometryExpand;
                ExpandUvs = expandUvs;
                WriteConstants = writeConstants;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct OutlineBufferType
        {
            public Color4 OutlineColor;
            public Vector2 TextureSize;
            public float OutlineThickness;
            public float Padding;
            public System.Numerics.Vector4 SourceUV;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DropShadowBufferType
        {
            public Vector2 ImgMin;
            public Vector2 ImgMax;
            public float ShadowSize;
            public float MaxAlpha;
            public Vector2 Padding;
        }

        public SharpDXD3D11SpriteRenderer(ID3D11Device device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _context = _device.ImmediateContext;

            InitializeShaders();
            InitializeBuffers();
            InitializeBlendStates();
            InitializeSampler();
        }

        private void InitializeShaders()
        {
            InitializeShader();
            InitializeOutlineShader();
            InitializeGrayscaleShader();
            InitializeDropShadowShader();
        }

        private void InitializeShader()
        {
            string shaderPath = FindShaderPath(ShaderFileName);

            if (string.IsNullOrEmpty(shaderPath) || !File.Exists(shaderPath))
                return;

            // Compile Vertex Shader
            Compiler.CompileFromFile(shaderPath, "VS", "vs_5_0", out Blob vertexShaderByteCode, out Blob errorBlob);
            if (vertexShaderByteCode != null)
            {
                _vertexShader = _device.CreateVertexShader(vertexShaderByteCode.AsBytes());
                _inputLayout = _device.CreateInputLayout(new[]
                {
                    new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
                    new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 8, 0),
                    new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 16, 0)
                }, vertexShaderByteCode.AsBytes());
                vertexShaderByteCode.Dispose();
            }

            // Compile Pixel Shader
            Compiler.CompileFromFile(shaderPath, "PS", "ps_5_0", out Blob pixelShaderByteCode, out errorBlob);
            if (pixelShaderByteCode != null)
            {
                _pixelShader = _device.CreatePixelShader(pixelShaderByteCode.AsBytes());
                pixelShaderByteCode.Dispose();
            }
        }

        private void InitializeGrayscaleShader()
        {
            string shaderPath = FindShaderPath(GrayscaleFileName);

            if (string.IsNullOrEmpty(shaderPath) || !File.Exists(shaderPath))
                return;

            // Compile Grayscale Pixel Shader
            Compiler.CompileFromFile(shaderPath, "PS_GRAY", "ps_5_0", out Blob grayscaleShaderByteCode, out Blob errorBlob);
            if (grayscaleShaderByteCode != null)
            {
                _grayscalePixelShader = _device.CreatePixelShader(grayscaleShaderByteCode.AsBytes());
                grayscaleShaderByteCode.Dispose();
            }
        }

        private void InitializeOutlineShader()
        {
            string shaderPath = FindShaderPath(OutlineShaderFileName);

            if (string.IsNullOrEmpty(shaderPath) || !File.Exists(shaderPath))
                return;

            Compiler.CompileFromFile(shaderPath, "PS_OUTLINE", "ps_5_0", out Blob pixelShaderByteCode, out Blob errorBlob);
            if (pixelShaderByteCode != null)
            {
                _outlinePixelShader = _device.CreatePixelShader(pixelShaderByteCode.AsBytes());
                pixelShaderByteCode.Dispose();
            }

            _outlineBuffer = _device.CreateBuffer(new BufferDescription
            {
                Usage = ResourceUsage.Dynamic,
                ByteWidth = Marshal.SizeOf<OutlineBufferType>(),
                BindFlags = BindFlags.ConstantBuffer,
                CPUAccessFlags = CpuAccessFlags.Write,
                MiscFlags = ResourceOptionFlags.None,
                StructureByteStride = 0
            });
        }

        private void InitializeDropShadowShader()
        {
            string shaderPath = FindShaderPath(DropShadowFileName);

            if (string.IsNullOrEmpty(shaderPath) || !File.Exists(shaderPath))
                return;

            Compiler.CompileFromFile(shaderPath, "PS_SHADOW", "ps_5_0", out Blob pixelShaderByteCode, out Blob errorBlob);
            if (pixelShaderByteCode != null)
            {
                _dropShadowPixelShader = _device.CreatePixelShader(pixelShaderByteCode.AsBytes());
                pixelShaderByteCode.Dispose();
            }

            _dropShadowBuffer = _device.CreateBuffer(new BufferDescription
            {
                Usage = ResourceUsage.Dynamic,
                ByteWidth = Marshal.SizeOf<DropShadowBufferType>(),
                BindFlags = BindFlags.ConstantBuffer,
                CPUAccessFlags = CpuAccessFlags.Write,
                MiscFlags = ResourceOptionFlags.None,
                StructureByteStride = 0
            });
        }

        private static string FindShaderPath(string filename)
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;

            string[] candidates = new[]
            {
                Path.Combine(baseDirectory, "Rendering", "SharpDXD3D11", "Shaders", filename)
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
            // Dynamic vertex buffer for quads (4 vertices)
            _vertexBuffer = _device.CreateBuffer(new BufferDescription
            {
                Usage = ResourceUsage.Dynamic,
                ByteWidth = Marshal.SizeOf<VertexType>() * 4,
                BindFlags = BindFlags.VertexBuffer,
                CPUAccessFlags = CpuAccessFlags.Write,
                MiscFlags = ResourceOptionFlags.None,
                StructureByteStride = 0
            });

            // Matrix constant buffer
            _matrixBuffer = _device.CreateBuffer(new BufferDescription
            {
                Usage = ResourceUsage.Dynamic,
                ByteWidth = Marshal.SizeOf<Matrix4x4>(), // Must be multiple of 16
                BindFlags = BindFlags.ConstantBuffer,
                CPUAccessFlags = CpuAccessFlags.Write,
                MiscFlags = ResourceOptionFlags.None,
                StructureByteStride = 0
            });
        }

        private void InitializeSampler()
        {
            _samplerState = _device.CreateSamplerState(new SamplerDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Clamp,
                AddressV = TextureAddressMode.Clamp,
                AddressW = TextureAddressMode.Clamp,
                ComparisonFunction = ComparisonFunction.Never,
                MinLOD = 0,
                MaxLOD = float.MaxValue
            });
        }

        private void InitializeBlendStates()
        {
            // Replicate DX9 default behavior (Screen Blend for both Color and Alpha) for falling-through modes
            // DX9: SourceBlend = InverseDestinationColor, DestinationBlend = One
            // Applied to both Color and Alpha channels.

            CreateBlendState(BlendMode.NORMAL, BlendOption.InverseDestinationColor, BlendOption.One, BlendOption.InverseDestinationAlpha, BlendOption.One);
            CreateBlendState(BlendMode.LIGHT, BlendOption.InverseDestinationColor, BlendOption.One, BlendOption.InverseDestinationAlpha, BlendOption.One);
            CreateBlendState(BlendMode.LIGHTINV, BlendOption.InverseDestinationColor, BlendOption.One, BlendOption.InverseDestinationAlpha, BlendOption.One);
            CreateBlendState(BlendMode.INVNORMAL, BlendOption.InverseDestinationColor, BlendOption.One, BlendOption.InverseDestinationAlpha, BlendOption.One);
            CreateBlendState(BlendMode.INVLIGHTINV, BlendOption.InverseDestinationColor, BlendOption.One, BlendOption.InverseDestinationAlpha, BlendOption.One);
            CreateBlendState(BlendMode.INVCOLOR, BlendOption.InverseDestinationColor, BlendOption.One, BlendOption.InverseDestinationAlpha, BlendOption.One);
            CreateBlendState(BlendMode.INVBACKGROUND, BlendOption.InverseDestinationColor, BlendOption.One, BlendOption.InverseDestinationAlpha, BlendOption.One);

            // Explicit mappings matching DX9
            // INVLIGHT: Source = BlendFactor, Destination = InverseSourceColor
            // Alpha: BlendFactor, InverseSourceAlpha
            CreateBlendState(BlendMode.INVLIGHT, BlendOption.BlendFactor, BlendOption.InverseSourceColor, BlendOption.BlendFactor, BlendOption.InverseSourceAlpha);

            // COLORFY: Source = SourceAlpha, Destination = One
            // Alpha: SourceAlpha, One
            CreateBlendState(BlendMode.COLORFY, BlendOption.SourceAlpha, BlendOption.One, BlendOption.SourceAlpha, BlendOption.One);

            // MASK: Source = Zero, Destination = InverseSourceAlpha
            // Alpha: Zero, InverseSourceAlpha
            CreateBlendState(BlendMode.MASK, BlendOption.Zero, BlendOption.InverseSourceAlpha, BlendOption.Zero, BlendOption.InverseSourceAlpha);

            // EFFECTMASK: Source = DestinationAlpha, Destination = One
            // Alpha: DestinationAlpha, One
            CreateBlendState(BlendMode.EFFECTMASK, BlendOption.DestinationAlpha, BlendOption.One, BlendOption.DestinationAlpha, BlendOption.One);

            // HIGHLIGHT: Source = BlendFactor, Destination = One
            // Alpha: BlendFactor, One
            CreateBlendState(BlendMode.HIGHLIGHT, BlendOption.BlendFactor, BlendOption.One, BlendOption.BlendFactor, BlendOption.One);

            // LIGHTMAP: Source = Zero, Destination = SourceColor
            // Alpha: Zero, SourceAlpha (SourceColor is invalid for Alpha, maps to SourceAlpha)
            CreateBlendState(BlendMode.LIGHTMAP, BlendOption.Zero, BlendOption.SourceColor, BlendOption.Zero, BlendOption.SourceAlpha);

            // NONE: Typically standard alpha or opaque.
            // In DX9, SetBlend(..., NONE) would fall through to default (Screen Blend) if called with Blending=true.
            // However, if Blending=false, it's standard AlphaBlend.
            // D3D11SpriteRenderer is only called when Blending=true.
            // To strictly replicate DX9 fall-through bug/feature, NONE should be Screen Blend.
            // But since NONE is filtered out in RenderingPipeline (handled by D2D), this might be unused.
            // We'll set it to Standard Alpha as a safe fallback or Screen if desired.
            // Let's stick to Standard Alpha for NONE to mean "Standard Blending" in this context.
            CreateBlendState(BlendMode.NONE, BlendOption.SourceAlpha, BlendOption.InverseSourceAlpha, BlendOption.SourceAlpha, BlendOption.InverseSourceAlpha);
        }

        private void CreateBlendState(BlendMode mode, BlendOption src, BlendOption dest, BlendOption srcAlpha, BlendOption destAlpha)
        {
            var desc = new BlendDescription();
            desc.RenderTarget[0].IsBlendEnabled = true;

            // Color Blending
            desc.RenderTarget[0].SourceBlend = src;
            desc.RenderTarget[0].DestinationBlend = dest;
            desc.RenderTarget[0].BlendOperation = BlendOperation.Add;

            // Alpha Blending
            // Use valid Alpha options passed in
            desc.RenderTarget[0].SourceBlendAlpha = srcAlpha;
            desc.RenderTarget[0].DestinationBlendAlpha = destAlpha;
            desc.RenderTarget[0].AlphaBlendOperation = BlendOperation.Add;

            desc.RenderTarget[0].RenderTargetWriteMask = ColorWriteEnable.All;

            _blendStates[mode] = _device.CreateBlendState(desc);
        }

        public bool SupportsOutlineShader => _outlinePixelShader != null && _outlineBuffer != null;

        public void Draw(ID3D11Texture2D texture, RectangleF destination, RectangleF? source, Color color, Matrix3x2 transform, BlendMode blendMode, float opacity, float blendRate)
        {
            DrawInternal(texture, destination, source, color, transform, blendMode, opacity, blendRate, _pixelShader, null);
        }

        public void DrawOutlined(ID3D11Texture2D texture, RectangleF destination, RectangleF? source, Color color, Matrix3x2 transform, BlendMode blendMode, float opacity, float blendRate, Color4 outlineColor, float outlineThickness)
        {
            var outlineEffect = CreateOutlineEffect(texture, source, outlineColor, outlineThickness);
            DrawInternal(texture, destination, source, color, transform, blendMode, opacity, blendRate, _outlinePixelShader ?? _pixelShader, outlineEffect);
        }

        public void DrawGrayscale(ID3D11Texture2D texture, RectangleF destination, RectangleF? source, Color color, Matrix3x2 transform, BlendMode blendMode, float opacity, float blendRate)
        {
            var grayscaleEffect = CreateGrayscaleEffect();
            DrawInternal(texture, destination, source, color, transform, blendMode, opacity, blendRate, _grayscalePixelShader ?? _pixelShader, grayscaleEffect);
        }

        public void DrawDropShadow(ID3D11Texture2D texture, RectangleF destination, RectangleF shadowBounds, RectangleF? source, Color color, Matrix3x2 transform, BlendMode blendMode, float opacity, float blendRate, Color4 shadowColor, float shadowWidth, float shadowMaxOpacity)
        {
            var dropShadowEffect = CreateDropShadowEffect(texture, shadowBounds, shadowWidth, shadowMaxOpacity);
            DrawInternal(texture, destination, source, color, transform, blendMode, opacity, blendRate, _dropShadowPixelShader ?? _pixelShader, dropShadowEffect);
        }

        private SpriteEffect? CreateOutlineEffect(ID3D11Texture2D texture, RectangleF? source, Color4 outlineColor, float outlineThickness)
        {
            if (!SupportsOutlineShader || _outlinePixelShader == null)
                return null;

            var texDesc = texture.Description;
            var texWidth = texDesc.Width;
            var texHeight = texDesc.Height;

            float u1 = 0, v1 = 0, u2 = 1, v2 = 1;
            if (source.HasValue)
            {
                u1 = source.Value.Left / texWidth;
                v1 = source.Value.Top / texHeight;
                u2 = source.Value.Right / texWidth;
                v2 = source.Value.Bottom / texHeight;
            }

            var outlineBuffer = new OutlineBufferType
            {
                OutlineColor = outlineColor,
                TextureSize = new Vector2(texWidth, texHeight),
                OutlineThickness = outlineThickness,
                Padding = 0f,
                SourceUV = new System.Numerics.Vector4(u1, v1, u2, v2)
            };

            return new SpriteEffect(
                _outlinePixelShader,
                _outlineBuffer,
                Marshal.SizeOf<OutlineBufferType>(),
                outlineThickness,
                true,
                ptr =>
                {
                    Marshal.StructureToPtr(outlineBuffer, ptr, false);
                });
        }

        private SpriteEffect? CreateGrayscaleEffect()
        {
            if (_grayscalePixelShader == null)
                return null;

            return new SpriteEffect(_grayscalePixelShader, null, 0, 0f, false, null);
        }

        private SpriteEffect? CreateDropShadowEffect(ID3D11Texture2D texture, RectangleF shadowBounds, float shadowWidth, float shadowMaxOpacity)
        {
            if (_dropShadowPixelShader == null || _dropShadowBuffer == null)
                return null;

            var shadowBuffer = new DropShadowBufferType
            {
                ImgMin = new Vector2(shadowBounds.Left, shadowBounds.Top),
                ImgMax = new Vector2(shadowBounds.Right, shadowBounds.Bottom),
                ShadowSize = shadowWidth,
                MaxAlpha = shadowMaxOpacity,
                Padding = Vector2.Zero
            };

            return new SpriteEffect(
                _dropShadowPixelShader,
                _dropShadowBuffer,
                Marshal.SizeOf<DropShadowBufferType>(),
                shadowWidth,
                false,
                ptr =>
                {
                    Marshal.StructureToPtr(shadowBuffer, ptr, false);
                });
        }

        private void DrawInternal(ID3D11Texture2D texture, RectangleF destination, RectangleF? source, Color color, Matrix3x2 transform, BlendMode blendMode, float opacity, float blendRate, ID3D11PixelShader pixelShader, SpriteEffect? effect)
        {
            if (texture == null) return;

            var activePixelShader = effect?.Shader ?? pixelShader ?? _pixelShader;
            if (activePixelShader == null) return;

            ID3D11RenderTargetView[] rtvs = new ID3D11RenderTargetView[1];
            ID3D11DepthStencilView dsv;
            _context.OMGetRenderTargets(1, rtvs, out dsv);
            
            if (rtvs[0] != null)
            {
                ID3D11Resource res = rtvs[0].Resource;
                if (res != null)
                {
                    ID3D11Texture2D tex = res.QueryInterface<ID3D11Texture2D>();
                    if (tex != null)
                    {
                        var desc = tex.Description;
                        var width = desc.Width;
                        var height = desc.Height;
                        _context.RSSetViewport(0, 0, width, height);
                        tex.Dispose();
                    }
                    res.Dispose();
                }
                rtvs[0].Dispose();
            }
            dsv?.Dispose();

            if (!_srvCache.TryGetValue(texture, out var srv))
            {
                srv = _device.CreateShaderResourceView(texture);
                _srvCache[texture] = srv;
            }

            _context.IASetInputLayout(_inputLayout);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
            _context.IASetVertexBuffers(0, 1, new[] { _vertexBuffer }, new[] { Marshal.SizeOf<VertexType>() }, new[] { 0 });

            bool usingEffect = effect.HasValue && effect.Value.IsValid;
            float geometryExpand = usingEffect ? effect.Value.GeometryExpand : 0f;
            bool expandUvs = usingEffect && effect.Value.ExpandUvs;

            Texture2DDescription texDesc = texture.Description;
            UpdateVertexBuffer(destination, source, texDesc.Width, texDesc.Height, color, opacity, geometryExpand, expandUvs);
            UpdateMatrixBuffer(transform);

            _context.VSSetShader(_vertexShader);
            _context.PSSetShader(activePixelShader);
            _context.PSSetShaderResources(0, 1, new[] { srv });
            _context.PSSetSamplers(0, 1, new[] { _samplerState });
            _context.VSSetConstantBuffers(0, 1, new[] { _matrixBuffer });

            ApplyEffect(effect);

            if (_blendStates.TryGetValue(blendMode, out var blendState))
            {
                var factor = new Color4(blendRate, blendRate, blendRate, blendRate);
                _context.OMSetBlendState(blendState, factor, -1);
            }
            else
            {
                var factor = new Color4(blendRate, blendRate, blendRate, blendRate);
                _context.OMSetBlendState(_blendStates[BlendMode.NORMAL], factor, -1);
            }

            _context.Draw(4, 0);

            _context.PSSetConstantBuffers(1, 1, new ID3D11Buffer[] { null });
            _context.OMSetBlendState(null, null, -1);
        }

        private void ApplyEffect(SpriteEffect? effect)
        {
            if (!effect.HasValue || !effect.Value.IsValid)
            {
                _context.PSSetConstantBuffers(1, 1, new ID3D11Buffer[] { null });
                return;
            }

            var spriteEffect = effect.Value;

            if (spriteEffect.ConstantBuffer != null && spriteEffect.WriteConstants != null)
            {
                MappedSubresource mapped = _context.Map(spriteEffect.ConstantBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
                spriteEffect.WriteConstants(mapped.DataPointer);
                _context.Unmap(spriteEffect.ConstantBuffer, 0);

                _context.PSSetConstantBuffers(1, 1, new[] { spriteEffect.ConstantBuffer });
            }
            else
            {
                _context.PSSetConstantBuffers(1, 1, new ID3D11Buffer[] { null });
            }
        }

        private void UpdateVertexBuffer(RectangleF dest, RectangleF? source, int texWidth, int texHeight, Color color, float opacity, float geometryExpand, bool expandUvs)
        {
            float left = dest.Left;
            float right = dest.Right;
            float top = dest.Top;
            float bottom = dest.Bottom;

            float u1 = 0, v1 = 0, u2 = 1, v2 = 1;
            if (source.HasValue)
            {
                u1 = source.Value.Left / (float)texWidth;
                v1 = source.Value.Top / (float)texHeight;
                u2 = source.Value.Right / (float)texWidth;
                v2 = source.Value.Bottom / (float)texHeight;
            }

            if (geometryExpand > 0)
            {
                left -= geometryExpand;
                right += geometryExpand;
                top -= geometryExpand;
                bottom += geometryExpand;

                if (expandUvs)
                {
                    float uPad = geometryExpand / texWidth;
                    float vPad = geometryExpand / texHeight;
                    u1 -= uPad;
                    v1 -= vPad;
                    u2 += uPad;
                    v2 += vPad;
                }
            }

            var col = new Color4(color.R / 255f, color.G / 255f, color.B / 255f, (color.A / 255f) * opacity);

            // Triangle Strip: TopLeft, TopRight, BottomLeft, BottomRight
            var v0 = new VertexType(new Vector2(left, top), new Vector2(u1, v1), col);
            var v1_ = new VertexType(new Vector2(right, top), new Vector2(u2, v1), col); // v1 is reserved
            var v2_ = new VertexType(new Vector2(left, bottom), new Vector2(u1, v2), col);
            var v3 = new VertexType(new Vector2(right, bottom), new Vector2(u2, v2), col);

            MappedSubresource mapped = _context.Map(_vertexBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
            unsafe
            {
                VertexType* vertices = (VertexType*)mapped.DataPointer;
                vertices[0] = v0;
                vertices[1] = v1_;
                vertices[2] = v2_;
                vertices[3] = v3;
            }
            _context.Unmap(_vertexBuffer, 0);
        }

        private void UpdateMatrixBuffer(Matrix3x2 transform)
        {
            // Create Orthographic Projection Matrix based on BackBuffer size
            Viewport[] viewports = new Viewport[1];
            int numViewports = 1;
            _context.RSGetViewports(ref numViewports, viewports);
            float width = viewports[0].Width;
            float height = viewports[0].Height;

            // Standard 2D Ortho: Top-Left (0,0) to Bottom-Right (w,h)
            // Map 0..W to -1..1, 0..H to 1..-1

            // Direct3D NDC: -1 to 1.
            // X: (x / W) * 2 - 1
            // Y: -((y / H) * 2 - 1) = 1 - (y / H) * 2

            Matrix4x4 projection = Matrix4x4.Identity;
            projection.M11 = 2.0f / width;
            projection.M22 = -2.0f / height;
            projection.M41 = -1.0f;
            projection.M42 = 1.0f;

            // Apply the object transform (Matrix3x2)
            // 3x2 = [ M11 M12 ]
            //       [ M21 M22 ]
            //       [ M31 M32 ]
            // Extend to 4x4
            Matrix4x4 world = Matrix4x4.Identity;
            world.M11 = transform.M11;
            world.M12 = transform.M12;
            world.M21 = transform.M21;
            world.M22 = transform.M22;
            world.M41 = transform.M31; // Translation X
            world.M42 = transform.M32; // Translation Y

            // Final Matrix = World * Projection
            // Transpose because HLSL defaults to column-major in mul(v, M) or row-major?
            // SharpDX matrices are Row-Major. HLSL mul(vector, matrix) expects Row-Major matrix if vector is row.
            // mul(float4, matrix) -> Row Vector * Matrix.
            // So we send it as is.

            Matrix4x4 final = world * projection;

            // However, D3D default constant buffer layout expects Column-Major logic if we don't transpose,
            // OR we construct it carefully.
            // SharpDX Matrix.Transpose() is usually needed if the shader uses `matrix` type and `mul(pos, mat)`.
            final = Matrix4x4.Transpose(final);

            MappedSubresource mapped = _context.Map(_matrixBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
            unsafe
            {
                Matrix4x4* matrix = (Matrix4x4*)mapped.DataPointer;
                *matrix = final;
            }
            _context.Unmap(_matrixBuffer, 0);
        }

        public void Dispose()
        {
            foreach (var srv in _srvCache.Values) srv.Dispose();
            _srvCache.Clear();

            foreach (var bs in _blendStates.Values) bs.Dispose();
            _blendStates.Clear();

            _samplerState?.Dispose();
            _matrixBuffer?.Dispose();
            _outlineBuffer?.Dispose();
            _dropShadowBuffer?.Dispose();
            _vertexBuffer?.Dispose();
            _inputLayout?.Dispose();
            _pixelShader?.Dispose();
            _grayscalePixelShader?.Dispose();
            _outlinePixelShader?.Dispose();
            _dropShadowPixelShader?.Dispose();
            _vertexShader?.Dispose();
        }
    }
}
