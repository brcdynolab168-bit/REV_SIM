using System.Runtime.InteropServices;

namespace RevGame;

/// <summary>
/// เล่นเสียงแบบสตรีมผ่าน winmm waveOut (มีใน Windows ทุกเครื่อง ไม่ต้องลง NuGet)
/// mono 16-bit, บัฟเฟอร์เล็กหลายก้อนเพื่อให้ดีเลย์ต่ำ
/// </summary>
internal sealed class WaveOutPlayer : IDisposable
{
    public delegate void FillHandler(short[] buffer, int count);

    private const int BufferCount = 4;
    private const int BufferSamples = 512;          // ~11.6 ms ต่อก้อน
    private const int CallbackEvent = 0x00050000;
    private const int WhdrInQueue = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHdr
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr hWaveOut, IntPtr uDeviceID, ref WaveFormatEx lpFormat,
        IntPtr dwCallback, IntPtr dwInstance, int dwFlags);

    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutReset(IntPtr hWaveOut);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr hWaveOut);

    private static readonly int HdrSize = Marshal.SizeOf<WaveHdr>();
    private static readonly int FlagsOffset = (int)Marshal.OffsetOf<WaveHdr>(nameof(WaveHdr.dwFlags));

    private readonly FillHandler _fill;
    private readonly AutoResetEvent _event = new(false);
    private readonly IntPtr[] _headers = new IntPtr[BufferCount];
    private readonly IntPtr[] _data = new IntPtr[BufferCount];
    private readonly short[] _managed = new short[BufferSamples];
    private IntPtr _handle;
    private Thread? _thread;
    private volatile bool _running;

    public WaveOutPlayer(int sampleRate, FillHandler fill)
    {
        _fill = fill;

        var fmt = new WaveFormatEx
        {
            wFormatTag = 1,                         // PCM
            nChannels = 1,
            nSamplesPerSec = (uint)sampleRate,
            wBitsPerSample = 16,
            nBlockAlign = 2,
            nAvgBytesPerSec = (uint)(sampleRate * 2),
            cbSize = 0,
        };

        int err = waveOutOpen(out _handle, new IntPtr(-1), ref fmt,
            _event.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CallbackEvent);
        if (err != 0) throw new InvalidOperationException("เปิดอุปกรณ์เสียงไม่ได้ (waveOutOpen error " + err + ")");

        int bytes = BufferSamples * 2;
        for (int i = 0; i < BufferCount; i++)
        {
            _data[i] = Marshal.AllocHGlobal(bytes);
            _headers[i] = Marshal.AllocHGlobal(HdrSize);
            var hdr = new WaveHdr { lpData = _data[i], dwBufferLength = (uint)bytes };
            Marshal.StructureToPtr(hdr, _headers[i], false);
            waveOutPrepareHeader(_handle, _headers[i], HdrSize);
        }

        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "Audio" };
        _thread.Start();
    }

    private void Loop()
    {
        while (_running)
        {
            for (int i = 0; i < BufferCount && _running; i++)
            {
                int flags = Marshal.ReadInt32(_headers[i], FlagsOffset);
                if ((flags & WhdrInQueue) != 0) continue;       // ก้อนนี้ยังเล่นไม่จบ

                _fill(_managed, BufferSamples);
                Marshal.Copy(_managed, 0, _data[i], BufferSamples);
                waveOutWrite(_handle, _headers[i], HdrSize);
            }
            _event.WaitOne(20);                                 // waveOut ปลุกเมื่อเล่นจบแต่ละก้อน
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        _running = false;
        _event.Set();
        _thread?.Join(500);

        waveOutReset(_handle);
        for (int i = 0; i < BufferCount; i++)
        {
            waveOutUnprepareHeader(_handle, _headers[i], HdrSize);
            Marshal.FreeHGlobal(_headers[i]);
            Marshal.FreeHGlobal(_data[i]);
        }
        waveOutClose(_handle);
        _handle = IntPtr.Zero;
        _event.Dispose();
    }
}
