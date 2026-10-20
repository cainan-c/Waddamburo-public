using Waddamburo.Platform.Prospero.Native;

namespace Waddamburo.Platform.Sdl.Media;

// These public names preserve the upstream engine contract. The implementation
// owns a PS5 AudioOut port and native mixer; it has no SDL dependency.
public readonly record struct SdlAudioFormat(int SampleRate, int Channels);

public sealed unsafe class SdlAudioDevice : IAudioOutput
{
    public const int DefaultSampleRate = 48000, DefaultChannels = 2;
    private readonly object _gate = new();
    private nint _stream;
    private bool _disposed;
    private ulong _submittedFrames;
    private readonly List<DirectVoice> _directVoices = [];
    public SdlAudioDevice(int sampleRate = DefaultSampleRate, int channels = DefaultChannels, int requestedBufferFrames = 256)
    {
        if (sampleRate != 48000 || channels != 2)
            throw new ArgumentException("The PS5 native mixer accepts 48000 Hz stereo float PCM.");
        Format = HardwareFormat = new(sampleRate, channels);
        HardwareBufferFrames = Math.Clamp((requestedBufferFrames + 255) / 256 * 256, 256, 2048);
        int result = NativeAudio.Open(HardwareBufferFrames);
        if (result < 0) throw new InvalidOperationException($"PS5 AudioOut could not open: 0x{result:x8}.");
        _stream = NativeAudio.Create();
        if (_stream == 0) { NativeAudio.Close(); throw new OutOfMemoryException("PS5 audio stream allocation failed."); }
    }
    public SdlAudioFormat Format { get; }
    public SdlAudioFormat HardwareFormat { get; }
    public int HardwareBufferFrames { get; }
    public string Driver => "PS5 AudioOut (native mixer)";
    public ulong SubmittedFrames { get { lock (_gate) return _submittedFrames; } }
    public ulong QueuedFrames { get { lock (_gate) { Ensure(); return (ulong)NativeAudio.Queued(_stream) / 8; } } }
    public void Resume() { lock (_gate) { Ensure(); NativeAudio.Pause(0); } }
    public void Pause() { lock (_gate) { Ensure(); NativeAudio.Pause(1); } }
    public void Queue(ReadOnlySpan<float> samples)
    {
        if (samples.Length % 2 != 0) throw new ArgumentException("Stereo samples must contain whole frames.");
        lock (_gate)
        {
            Ensure();
            if (samples.IsEmpty) return;
            fixed (float* source = samples)
                if (NativeAudio.Queue(_stream, (nint)source, checked(samples.Length * sizeof(float))) != 0)
                    throw new OutOfMemoryException("The native audio queue could not grow.");
            _submittedFrames += (ulong)(samples.Length / 2);
        }
    }
    public DirectVoice CreateDirectVoice()
    {
        lock (_gate)
        {
            Ensure();
            nint stream = NativeAudio.Create();
            if (stream == 0) throw new OutOfMemoryException("Native voice allocation failed.");
            var voice = new DirectVoice(stream, 2); _directVoices.Add(voice); return voice;
        }
    }
    public void Flush() { lock (_gate) Ensure(); } // Float PCM is already in the hardware mix format.
    public void Clear() { lock (_gate) { Ensure(); NativeAudio.Clear(_stream); } }
    private void Ensure() => ObjectDisposedException.ThrowIf(_disposed || _stream == 0, this);
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var voice in _directVoices) voice.Destroy();
            _directVoices.Clear(); NativeAudio.Destroy(_stream); _stream = 0; NativeAudio.Close();
        }
    }
}

public sealed unsafe class DirectVoice
{
    private nint _stream;
    private readonly int _channels;
    private float[] _queued = [], _spare = [];
    private int _queuedLength;
    internal DirectVoice(nint stream, int channels) { _stream = stream; _channels = channels; }
    public bool IsPlaying => _stream != 0 && NativeAudio.Queued(_stream) > 0;
    public void Play(ReadOnlySpan<float> samples, float gain) => Replace(samples, gain, -1);
    public void Stop(int fadeFrames) => Replace([], 0, fadeFrames);
    private void Replace(ReadOnlySpan<float> samples, float gain, int fadeFrames)
    {
        if (_stream == 0) return;
        NativeAudio.Lock(_stream);
        try
        {
            int queued = Math.Min(_queuedLength, NativeAudio.Queued(_stream) / sizeof(float));
            queued -= queued % _channels;
            var tail = _queued.AsSpan(_queuedLength - queued, queued);
            if (fadeFrames >= 0) tail = tail[..Math.Min(tail.Length, fadeFrames * _channels)];
            int length = Math.Max(samples.Length, tail.Length);
            if (_spare.Length < length) _spare = new float[length];
            var next = _spare.AsSpan(0, length); next.Clear();
            for (int i=0; i<samples.Length; i++) next[i] = samples[i] * gain;
            for (int i=0; i<tail.Length; i++) next[i] += fadeFrames > 0 ? tail[i] * (1f - (float)(i/_channels)/fadeFrames) : tail[i];
            NativeAudio.Clear(_stream);
            if (length > 0)
                fixed (float* source = next)
                    if (NativeAudio.Queue(_stream, (nint)source, checked(length * sizeof(float))) != 0)
                        throw new OutOfMemoryException("Native voice queue failed.");
            (_queued, _spare) = (_spare, _queued); _queuedLength = length;
        }
        finally { NativeAudio.Unlock(_stream); }
    }
    internal void Destroy() { if (_stream != 0) { NativeAudio.Destroy(_stream); _stream = 0; } }
}
