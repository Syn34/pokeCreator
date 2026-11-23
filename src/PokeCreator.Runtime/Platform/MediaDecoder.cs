using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PokeCreator.Runtime.Platform
{
    /// <summary>
    /// Audio SDL_mixer can't read itself (MP4 / M4A / AAC / WMA) is decoded with Windows Media Foundation into a
    /// WAV file in a cache folder the first time it's played; after that the cached WAV is used straight away.
    /// </summary>
    public static class MediaDecoder
    {
        static readonly string[] Extensions = { ".mp4", ".m4a", ".aac", ".wma" };

        public static bool NeedsDecoding(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return Array.IndexOf(Extensions, ext) >= 0;
        }

        public static string CacheFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokeCreator", "AudioCache"); }
        }

        /// <summary>A path SDL_mixer can play: the file itself, or its decoded WAV copy (null if it couldn't be decoded).</summary>
        public static string Playable(string path)
        {
            if (!NeedsDecoding(path)) return path;
            try
            {
                var fi = new FileInfo(path);
                string key = Hash(fi.FullName.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks);
                string wav = Path.Combine(CacheFolder, Path.GetFileNameWithoutExtension(path) + "-" + key + ".wav");
                if (File.Exists(wav)) return wav;
                Directory.CreateDirectory(CacheFolder);
                var start = DateTime.Now;
                string tmp = wav + ".part";
                DecodeToWav(path, tmp);
                if (File.Exists(wav)) File.Delete(wav);
                File.Move(tmp, wav);
                Console.WriteLine("Decoded " + Path.GetFileName(path) + " for playback (" + (int)(DateTime.Now - start).TotalMilliseconds + " ms, cached)");
                return wav;
            }
            catch (DllNotFoundException) { Console.WriteLine("Can't play " + Path.GetFileName(path) + ": MP4/M4A audio needs Windows Media Foundation (Windows; on Windows N editions install the Media Feature Pack). Use OGG instead."); }
            catch (EntryPointNotFoundException) { Console.WriteLine("Can't play " + Path.GetFileName(path) + ": Windows Media Foundation isn't available. Use OGG instead."); }
            catch (Exception ex) { Console.WriteLine("Can't play " + Path.GetFileName(path) + ": " + ex.Message); }
            return null;
        }

        static string Hash(string s)
        {
            using (var sha = SHA1.Create())
            {
                var b = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder();
                for (int i = 0; i < 6; i++) sb.Append(b[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // ------------------------------------------------------------------ Media Foundation

        const int MF_VERSION = 0x00020070;
        const int MFSTARTUP_FULL = 0;
        const int FIRST_AUDIO_STREAM = unchecked((int)0xFFFFFFFD);
        const int ALL_STREAMS = unchecked((int)0xFFFFFFFE);
        const int READERF_ENDOFSTREAM = 0x2;
        const int READERF_ERROR = 0x1;

        static Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        static Guid MF_MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        static Guid MFMediaType_Audio = new Guid("73647561-0000-0010-8000-00AA00389B71");
        static Guid MFAudioFormat_PCM = new Guid("00000001-0000-0010-8000-00AA00389B71");
        static Guid MF_MT_AUDIO_NUM_CHANNELS = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a");
        static Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
        static Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");

        [DllImport("mfplat.dll", ExactSpelling = true)] static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll", ExactSpelling = true)] static extern int MFShutdown();
        [DllImport("mfplat.dll", ExactSpelling = true)] static extern int MFCreateMediaType(out IMFMediaType ppMFType);
        [DllImport("mfreadwrite.dll", ExactSpelling = true)] static extern int MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr attributes, out IMFSourceReader reader);

        /// <summary>Decodes the first audio stream to 16-bit PCM and writes it as a WAV file.</summary>
        static void DecodeToWav(string src, string dst)
        {
            Check(MFStartup(MF_VERSION, MFSTARTUP_FULL), "MFStartup");
            IMFSourceReader reader = null; IMFMediaType want = null, got = null;
            try
            {
                Check(MFCreateSourceReaderFromURL(Path.GetFullPath(src), IntPtr.Zero, out reader), "open the file");
                reader.SetStreamSelection(ALL_STREAMS, false);
                reader.SetStreamSelection(FIRST_AUDIO_STREAM, true);
                Check(MFCreateMediaType(out want), "MFCreateMediaType");
                want.SetGUID(ref MF_MT_MAJOR_TYPE, ref MFMediaType_Audio);
                want.SetGUID(ref MF_MT_SUBTYPE, ref MFAudioFormat_PCM);
                want.SetUINT32(ref MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                reader.SetCurrentMediaType(FIRST_AUDIO_STREAM, IntPtr.Zero, want);
                reader.SetStreamSelection(FIRST_AUDIO_STREAM, true);
                reader.GetCurrentMediaType(FIRST_AUDIO_STREAM, out got);
                int channels, rate, bits;
                got.GetUINT32(ref MF_MT_AUDIO_NUM_CHANNELS, out channels);
                got.GetUINT32(ref MF_MT_AUDIO_SAMPLES_PER_SECOND, out rate);
                got.GetUINT32(ref MF_MT_AUDIO_BITS_PER_SAMPLE, out bits);
                if (channels <= 0 || rate <= 0) throw new InvalidDataException("no audio in the file");
                if (bits <= 0) bits = 16;

                using (var fs = new FileStream(dst, FileMode.Create, FileAccess.Write))
                using (var w = new BinaryWriter(fs))
                {
                    WriteHeader(w, channels, rate, bits, 0);
                    long dataBytes = 0;
                    var copy = new byte[0];
                    while (true)
                    {
                        int actual, flags; long time; IMFSample sample;
                        reader.ReadSample(FIRST_AUDIO_STREAM, 0, out actual, out flags, out time, out sample);
                        if ((flags & READERF_ERROR) != 0) { Release(sample); throw new InvalidDataException("the file couldn't be decoded"); }
                        if (sample != null)
                        {
                            IMFMediaBuffer buf = null;
                            try
                            {
                                sample.ConvertToContiguousBuffer(out buf);
                                IntPtr p; int max, len;
                                buf.Lock(out p, out max, out len);
                                try
                                {
                                    if (copy.Length < len) copy = new byte[len];
                                    Marshal.Copy(p, copy, 0, len);
                                }
                                finally { buf.Unlock(); }
                                w.Write(copy, 0, len);
                                dataBytes += len;
                            }
                            finally { Release(buf); Release(sample); }
                        }
                        if ((flags & READERF_ENDOFSTREAM) != 0) break;
                    }
                    if (dataBytes == 0) throw new InvalidDataException("no audio in the file");
                    fs.Seek(0, SeekOrigin.Begin);
                    WriteHeader(w, channels, rate, bits, dataBytes);
                }
            }
            finally
            {
                Release(got); Release(want); Release(reader);
                MFShutdown();
            }
        }

        static void WriteHeader(BinaryWriter w, int channels, int rate, int bits, long dataBytes)
        {
            int block = channels * bits / 8;
            uint data = (uint)Math.Min(dataBytes, uint.MaxValue - 36);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data);
            w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)channels);
            w.Write(rate); w.Write(rate * block); w.Write((short)block); w.Write((short)bits);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data);
        }

        /// <summary>For tests: writes a WAV header + samples the same way the decoder does.</summary>
        public static void WriteWav(string dst, int channels, int rate, int bits, byte[] samples)
        {
            using (var fs = new FileStream(dst, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(fs)) { WriteHeader(w, channels, rate, bits, samples.Length); w.Write(samples); }
        }

        static void Check(int hr, string what) { if (hr < 0) throw new IOException("Media Foundation couldn't " + what + " (0x" + hr.ToString("X8") + ")"); }
        static void Release(object o) { if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }

        // Interfaces: every vtable slot is declared in order (unused ones as stubs) so the calls land on the right methods.

        [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFMediaType
        {
            // IMFAttributes
            void GetItem(); void GetItemType(); void CompareItem(); void Compare();
            void GetUINT32([In] ref Guid guidKey, out int punValue);
            void GetUINT64(); void GetDouble(); void GetGUID(); void GetStringLength(); void GetString(); void GetAllocatedString();
            void GetBlobSize(); void GetBlob(); void GetAllocatedBlob(); void GetUnknown(); void SetItem(); void DeleteItem(); void DeleteAllItems();
            void SetUINT32([In] ref Guid guidKey, int unValue);
            void SetUINT64();
            void SetDouble();
            void SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
            void SetString(); void SetBlob(); void SetUnknown(); void LockStore(); void UnlockStore(); void GetCount(); void GetItemByIndex(); void CopyAllItems();
        }

        [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFSourceReader
        {
            void GetStreamSelection();
            void SetStreamSelection(int dwStreamIndex, [MarshalAs(UnmanagedType.Bool)] bool fSelected);
            void GetNativeMediaType();
            void GetCurrentMediaType(int dwStreamIndex, out IMFMediaType ppMediaType);
            void SetCurrentMediaType(int dwStreamIndex, IntPtr pdwReserved, IMFMediaType pMediaType);
            void SetCurrentPosition();
            void ReadSample(int dwStreamIndex, int dwControlFlags, out int pdwActualStreamIndex, out int pdwStreamFlags, out long pllTimestamp, out IMFSample ppSample);
        }

        [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFSample
        {
            // IMFAttributes (30)
            void a01(); void a02(); void a03(); void a04(); void a05(); void a06(); void a07(); void a08(); void a09(); void a10();
            void a11(); void a12(); void a13(); void a14(); void a15(); void a16(); void a17(); void a18(); void a19(); void a20();
            void a21(); void a22(); void a23(); void a24(); void a25(); void a26(); void a27(); void a28(); void a29(); void a30();
            // IMFSample
            void GetSampleFlags(); void SetSampleFlags(); void GetSampleTime(); void SetSampleTime(); void GetSampleDuration(); void SetSampleDuration();
            void GetBufferCount(); void GetBufferByIndex();
            void ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
        }

        [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFMediaBuffer
        {
            void Lock(out IntPtr ppbBuffer, out int pcbMaxLength, out int pcbCurrentLength);
            void Unlock();
        }
    }
}
