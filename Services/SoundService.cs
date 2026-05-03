using System.ComponentModel;
using System.IO;
using System.Text;

namespace FileTinder.Services;

/// <summary>
/// Generates and plays short synthesised tones for UI feedback.
/// No external audio files required — PCM samples are built in-memory.
/// </summary>
public class SoundService : INotifyPropertyChanged
{
    public static readonly SoundService Instance = new();

    private bool   _isMuted = true; // muted by default
    private double _volume = 0.55; // 0.0 – 1.0

    public bool IsMuted
    {
        get => _isMuted;
        set { _isMuted = value; OnPropertyChanged(nameof(IsMuted)); OnPropertyChanged(nameof(MuteIcon)); }
    }

    public double Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0.0, 1.0); OnPropertyChanged(nameof(Volume)); }
    }

    /// Returns speaker icon string matching mute state (for binding)
    public string MuteIcon => _isMuted ? "🔇" : "🔊";

    // ── Public play methods ────────────────────────────────────────────────────

    public void PlayKeep()     => Play(660, 70,  0.75); // bright C5-ish chime
    public void PlayDelete()   => Play(180, 90,  0.60); // low thud
    public void PlayBucket()   => Play(523, 60,  0.55); // mid ding
    public void PlaySnapBack() => Play(380, 38,  0.28); // subtle tick

    // ── Core ──────────────────────────────────────────────────────────────────

    private void Play(int freqHz, int durationMs, double baseAmplitude)
    {
        if (_isMuted || _volume <= 0.0) return;

        var amp = baseAmplitude * _volume;
        Task.Run(() =>
        {
            try
            {
                var wav = BuildToneWav(freqHz, durationMs, amp);
                using var ms = new MemoryStream(wav);
                using var sp = new System.Media.SoundPlayer(ms);
                sp.PlaySync();
            }
            catch { /* swallow — sound is non-critical */ }
        });
    }

    /// Synthesises a short sine-wave tone with a smooth attack/release envelope.
    private static byte[] BuildToneWav(int freqHz, int durationMs, double amplitude)
    {
        const int SampleRate = 22050;
        const int Channels   = 1;
        const int BitsPerSample = 16;

        int numSamples = SampleRate * durationMs / 1000;
        // Fade in/out over whichever is shorter: 10% of the tone or 80 samples
        int fadeLen = Math.Min(numSamples / 10 + 1, 80);

        var samples = new short[numSamples];
        for (int i = 0; i < numSamples; i++)
        {
            double t       = i / (double)SampleRate;
            double sine    = Math.Sin(2 * Math.PI * freqHz * t);
            double env     = Math.Min(1.0, Math.Min(i / (double)fadeLen, (numSamples - i) / (double)fadeLen));
            samples[i]     = (short)(sine * amplitude * 32767 * env);
        }

        int dataBytes = numSamples * (BitsPerSample / 8) * Channels;
        using var ms = new MemoryStream(44 + dataBytes);
        using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        // RIFF header
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataBytes);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);                        // chunk size
        bw.Write((short)1);                  // PCM
        bw.Write((short)Channels);
        bw.Write(SampleRate);
        bw.Write(SampleRate * Channels * (BitsPerSample / 8)); // byte rate
        bw.Write((short)(Channels * (BitsPerSample / 8)));     // block align
        bw.Write((short)BitsPerSample);

        // data chunk
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataBytes);
        foreach (var s in samples) bw.Write(s);

        return ms.ToArray();
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
