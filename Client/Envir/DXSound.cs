using Client.Rendering;
using Vortice.DirectSound;
using Vortice.Multimedia;
using System;
using System.Collections.Generic;
using System.IO;
using NAudioWave = NAudio.Wave;

namespace Client.Envir
{
    public sealed class DXSound : ISoundCacheItem
    {
        public string FileName { get; set; }

        public List<IDirectSoundBuffer> BufferList = new List<IDirectSoundBuffer>();

        private WaveFormat Format;
        private byte[] RawData;


        public DateTime ExpireTime { get; set; }
        public bool Loop { get; set; }

        public SoundType SoundType { get; set; }

        public int Volume { get; set; }

        public DXSound(string fileName, SoundType type)
        {
            FileName = fileName;
            SoundType = type;

            Volume = DXSoundManager.GetVolume(SoundType);
        }
        public void Play()
        {
            if (RawData == null)
            {
                if (!File.Exists(FileName))
                {
                    return;
                }

                if (string.Equals(Path.GetExtension(FileName), ".mp3", StringComparison.OrdinalIgnoreCase))
                {
                    using (var mp3 = new NAudioWave.Mp3FileReader(FileName))
                    {
                        Format = ConvertWaveFormat(mp3.WaveFormat);

                        RawData = ReadAllBytes(mp3);
                    }
                }
                else
                {
                    using (var waveReader = new NAudioWave.WaveFileReader(FileName))
                    {
                        Format = ConvertWaveFormat(waveReader.WaveFormat);
                        RawData = ReadAllBytes(waveReader);
                    }
                }
                RenderingPipelineManager.RegisterSoundCache(this);
            }


            if (BufferList.Count == 0)
            {
                CreateBuffer();
            }

            if (Loop)
            {
                if (!IsBufferPlaying(BufferList[0]))
                {
                    BufferList[0].Play(0, PlayFlags.Looping);
                }

                ExpireTime = DateTime.MaxValue;
                return;
            }
            ExpireTime = CEnvir.Now + Config.CacheDuration;

            for (int i = BufferList.Count - 1; i >= 0; i--)
            {
                if (IsBufferPlaying(BufferList[i]))
                {
                    continue;
                }

                BufferList[i].Play(0, PlayFlags.None);
                return;
            }

            if (BufferList.Count >= Config.SoundOverLap)
            {
                return;
            }

            IDirectSoundBuffer buff = CreateBuffer();
            buff.Play(0, PlayFlags.None);
        }
        public void Stop()
        {
            if (BufferList == null)
            {
                return;
            }

            if (Loop)
            {
                ExpireTime = CEnvir.Now + Config.CacheDuration;
            }

            for (int i = BufferList.Count - 1; i >= 0; i--)
            {
                BufferList[i].CurrentPosition = 0;
                BufferList[i].Stop();
            }
        }

        private IDirectSoundBuffer CreateBuffer()
        {
            IDirectSoundBuffer buff;
            BufferFlags flags = BufferFlags.ControlVolume;

            if (Config.SoundInBackground)
            {
                flags |= BufferFlags.GlobalFocus;
            }

            var description = new SoundBufferDescription
            {
                Format = Format,
                BufferBytes = RawData.Length,
                Flags = flags
            };

            buff = DXSoundManager.Device.CreateSoundBuffer(description);
            buff.Volume = Volume;
            BufferList.Add(buff);

            DataStream stream = buff.Lock(0, RawData.Length, LockFlags.EntireBuffer);
            stream.WriteRange(RawData);
            buff.Unlock(stream);

            return buff;
        }
        public void DisposeSoundBuffer()
        {
            RawData = null;

            for (int i = BufferList.Count - 1; i >= 0; i--)
            {
                BufferList[i]?.Dispose();
                BufferList.RemoveAt(i);
            }

            RenderingPipelineManager.UnregisterSoundCache(this);
            ExpireTime = DateTime.MinValue;
        }

        public void SetVolume()
        {
            Volume = DXSoundManager.GetVolume(SoundType);

            for (int i = BufferList.Count - 1; i >= 0; i--)
            {
                BufferList[i].Volume = Volume;
            }
        }

        public void UpdateFlags()
        {
            for (int i = BufferList.Count - 1; i >= 0; i--)
            {
                IDirectSoundBuffer buffer = CreateBuffer();

                buffer.CurrentPosition = GetCurrentPlayPosition(BufferList[0]);

                if (IsBufferPlaying(BufferList[0]))
                {
                    buffer.Play(0, Loop ? PlayFlags.Looping : PlayFlags.None);
                }

                BufferList[0]?.Dispose();
                BufferList.RemoveAt(0);
            }

        }

        private static bool IsBufferPlaying(IDirectSoundBuffer buffer)
        {
            return buffer.Status.HasFlag(BufferStatus.Playing);
        }

        private static int GetCurrentPlayPosition(IDirectSoundBuffer buffer)
        {
            buffer.GetCurrentPosition(out int playCursor, out _);
            return playCursor;
        }

        private static WaveFormat ConvertWaveFormat(global::NAudio.Wave.WaveFormat sourceFormat)
        {
            if (!Enum.TryParse(sourceFormat.Encoding.ToString(), out WaveFormatTag encoding))
                encoding = WaveFormatTag.Pcm;

            return WaveFormat.CreateCustomFormat(
                encoding,
                sourceFormat.SampleRate,
                sourceFormat.Channels,
                sourceFormat.AverageBytesPerSecond,
                sourceFormat.BlockAlign,
                sourceFormat.BitsPerSample);
        }

        private static byte[] ReadAllBytes(NAudioWave.WaveStream stream)
        {
            stream.Position = 0;

            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }
    }
}
