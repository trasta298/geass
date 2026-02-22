using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Geass.Services;

public class AudioCaptureService : IDisposable
{
    public enum AudioInputStatus
    {
        Listening,
        Muted,
        NoInput,
        Clipping
    }

    private static readonly TimeSpan MaxRecordingDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SilenceWarningDelay = TimeSpan.FromSeconds(0.9);
    private const float SignalThreshold = 0.015f;
    private const float ClippingThreshold = 0.98f;

    private WasapiCapture? _capture;
    private MMDevice? _captureDevice;
    private WaveFileWriter? _writer;
    private string? _rawFilePath;
    private string? _outputFilePath;
    private System.Threading.Timer? _maxDurationTimer;
    private System.Threading.Timer? _inputHealthTimer;
    private readonly List<string> _tempFiles = [];
    private DateTime _lastSignalAtUtc = DateTime.MinValue;
    private DateTime _lastClippingAtUtc = DateTime.MinValue;
    private DateTime _lastLevelEventAtUtc = DateTime.MinValue;
    private float _smoothedLevel;
    private AudioInputStatus _lastInputStatus = AudioInputStatus.Listening;

    public bool LastRecordingHadMeaningfulAudio { get; private set; }

    public event Action? RecordingStopped;
    public event Action<AudioInputStatus>? InputStatusChanged;
    public event Action<float>? InputLevelChanged;

    public void StartRecording()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Geass");
        Directory.CreateDirectory(tempDir);

        var id = Guid.NewGuid().ToString("N")[..8];
        _rawFilePath = Path.Combine(tempDir, $"{id}_raw.wav");
        _outputFilePath = Path.Combine(tempDir, $"{id}.wav");
        _tempFiles.Add(_rawFilePath);
        _tempFiles.Add(_outputFilePath);

        _captureDevice = GetDefaultCaptureDevice();
        // Keep capture initialization compatible with prior behavior.
        _capture = new WasapiCapture();
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnCaptureRecordingStopped;

        // Record in native WASAPI format (typically 32-bit float, 48kHz)
        // No per-buffer resampling - convert after recording completes
        _writer = new WaveFileWriter(_rawFilePath, _capture.WaveFormat);
        LastRecordingHadMeaningfulAudio = false;
        _lastSignalAtUtc = DateTime.UtcNow;
        _lastClippingAtUtc = DateTime.MinValue;
        _lastLevelEventAtUtc = DateTime.MinValue;
        _smoothedLevel = 0f;
        _lastInputStatus = AudioInputStatus.Listening;

        _capture.StartRecording();
        StartInputHealthMonitoring();

        _maxDurationTimer = new System.Threading.Timer(
            _ => StopRecordingInternal(),
            null,
            MaxRecordingDuration,
            Timeout.InfiniteTimeSpan);
    }

    public Task<string> StopRecording()
    {
        var tcs = new TaskCompletionSource<string>();
        var rawPath = _rawFilePath ?? throw new InvalidOperationException("No recording in progress");
        var outputPath = _outputFilePath ?? throw new InvalidOperationException("No recording in progress");

        if (_capture is { CaptureState: CaptureState.Capturing })
        {
            void Handler(object? sender, StoppedEventArgs e)
            {
                _capture!.RecordingStopped -= Handler;
                FinalizeRecording(rawPath, outputPath);
                tcs.TrySetResult(outputPath);
            }

            _capture.RecordingStopped += Handler;
            StopRecordingInternal();
        }
        else
        {
            FinalizeRecording(rawPath, outputPath);
            tcs.TrySetResult(outputPath);
        }

        return tcs.Task;
    }

    private void FinalizeRecording(string rawPath, string outputPath)
    {
        CloseWriter();

        try
        {
            ConvertToTargetFormat(rawPath, outputPath);
        }
        catch
        {
            // If conversion fails, use raw file as-is
            if (File.Exists(rawPath))
                File.Copy(rawPath, outputPath, true);
        }
    }

    private static void ConvertToTargetFormat(string inputPath, string outputPath)
    {
        var targetFormat = new WaveFormat(16000, 16, 1);

        using var reader = new AudioFileReader(inputPath);
        using var resampler = new MediaFoundationResampler(reader, targetFormat)
        {
            ResamplerQuality = 60
        };
        WaveFileWriter.CreateWaveFile(outputPath, resampler);
    }

    private void StopRecordingInternal()
    {
        _maxDurationTimer?.Dispose();
        _maxDurationTimer = null;
        _inputHealthTimer?.Dispose();
        _inputHealthTimer = null;

        try
        {
            _capture?.StopRecording();
        }
        catch
        {
            // Already stopped
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_writer is null || e.BytesRecorded == 0)
            return;

        try
        {
            var (activity, peak) = ComputeInputLevels(e.Buffer, e.BytesRecorded, _capture?.WaveFormat);
            var hasSignal = activity >= SignalThreshold;
            if (hasSignal)
            {
                LastRecordingHadMeaningfulAudio = true;
                _lastSignalAtUtc = DateTime.UtcNow;
            }

            if (peak >= ClippingThreshold)
            {
                _lastClippingAtUtc = DateTime.UtcNow;
            }

            // Smooth and throttle level updates to avoid UI churn.
            _smoothedLevel = (_smoothedLevel * 0.82f) + (activity * 0.18f);
            var now = DateTime.UtcNow;
            if ((now - _lastLevelEventAtUtc) >= TimeSpan.FromMilliseconds(70))
            {
                _lastLevelEventAtUtc = now;
                TryRaiseInputLevel(Math.Clamp(_smoothedLevel, 0f, 1f));
            }
        }
        catch
        {
            // Never crash capture due to metering logic.
        }

        // Trim leading silence by writing only after speech begins.
        if (LastRecordingHadMeaningfulAudio)
        {
            _writer.Write(e.Buffer, 0, e.BytesRecorded);
        }
    }

    private void OnCaptureRecordingStopped(object? sender, StoppedEventArgs e)
    {
        CloseWriter();
        TryRaiseInputLevel(0f);
        RecordingStopped?.Invoke();
    }

    private void CloseWriter()
    {
        _writer?.Dispose();
        _writer = null;
    }

    private MMDevice? GetDefaultCaptureDevice()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            try
            {
                return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            }
            finally
            {
                enumerator.Dispose();
            }
        }
        catch
        {
            return null;
        }
    }

    private void StartInputHealthMonitoring()
    {
        _inputHealthTimer?.Dispose();
        _inputHealthTimer = new System.Threading.Timer(_ => EvaluateInputHealth(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(350));
    }

    private void EvaluateInputHealth()
    {
        try
        {
            if (_capture is null || _capture.CaptureState != CaptureState.Capturing)
                return;

            var nowUtc = DateTime.UtcNow;
            AudioInputStatus status;
            var endpointVolume = _captureDevice?.AudioEndpointVolume?.MasterVolumeLevelScalar ?? 1f;

            if (_captureDevice?.AudioEndpointVolume?.Mute == true || endpointVolume <= 0.001f)
            {
                status = AudioInputStatus.Muted;
            }
            else if ((nowUtc - _lastSignalAtUtc) > SilenceWarningDelay)
            {
                // Some hardware mute keys map to near-zero endpoint volume.
                status = endpointVolume <= 0.05f ? AudioInputStatus.Muted : AudioInputStatus.NoInput;
            }
            else if ((nowUtc - _lastClippingAtUtc) < TimeSpan.FromSeconds(1.2))
            {
                status = AudioInputStatus.Clipping;
            }
            else
            {
                status = AudioInputStatus.Listening;
            }

            if (status == _lastInputStatus)
                return;

            _lastInputStatus = status;
            InputStatusChanged?.Invoke(status);
        }
        catch
        {
            // Keep recording resilient even if endpoint polling fails.
        }
    }

    private void TryRaiseInputLevel(float level)
    {
        try
        {
            InputLevelChanged?.Invoke(level);
        }
        catch
        {
            // UI subscriber issues should not terminate recording.
        }
    }

    private static (float activity, float peak) ComputeInputLevels(byte[] buffer, int bytesRecorded, WaveFormat? waveFormat)
    {
        if (waveFormat is null || bytesRecorded <= 0)
            return (0f, 0f);

        var peak = 0f;
        double sumSquares = 0;
        var sampleCount = 0;
        var bitsPerSample = waveFormat.BitsPerSample;
        var channels = Math.Max(1, waveFormat.Channels);

        if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat && bitsPerSample == 32)
        {
            for (var i = 0; i <= bytesRecorded - 4; i += 4)
            {
                var sample = BitConverter.ToSingle(buffer, i);
                var abs = MathF.Abs(sample);
                if (abs > peak) peak = abs;
                sumSquares += sample * sample;
                sampleCount++;
            }
            return ComputeActivity(peak, sumSquares, sampleCount);
        }

        if (bitsPerSample == 16)
        {
            for (var i = 0; i <= bytesRecorded - 2; i += 2)
            {
                var sample = BitConverter.ToInt16(buffer, i) / 32768f;
                var abs = MathF.Abs(sample);
                if (abs > peak) peak = abs;
                sumSquares += sample * sample;
                sampleCount++;
            }
            return ComputeActivity(peak, sumSquares, sampleCount);
        }

        if (bitsPerSample == 24)
        {
            for (var i = 0; i <= bytesRecorded - 3; i += 3)
            {
                var sample = (buffer[i + 2] << 24) | (buffer[i + 1] << 16) | (buffer[i] << 8);
                sample >>= 8;
                var normalized = sample / 8388608f;
                var abs = MathF.Abs(normalized);
                if (abs > peak) peak = abs;
                sumSquares += normalized * normalized;
                sampleCount++;
            }
            return ComputeActivity(peak, sumSquares, sampleCount);
        }

        if (bitsPerSample == 32)
        {
            for (var i = 0; i <= bytesRecorded - 4; i += 4)
            {
                var sample = BitConverter.ToInt32(buffer, i) / 2147483648f;
                var abs = MathF.Abs(sample);
                if (abs > peak) peak = abs;
                sumSquares += sample * sample;
                sampleCount++;
            }
            return ComputeActivity(peak, sumSquares, sampleCount);
        }

        // Fallback for uncommon formats: use average absolute byte amplitude.
        for (var i = 0; i < bytesRecorded; i += channels)
        {
            var normalized = MathF.Abs((buffer[i] - 128) / 128f);
            if (normalized > peak) peak = normalized;
            sumSquares += normalized * normalized;
            sampleCount++;
        }
        return ComputeActivity(peak, sumSquares, sampleCount);
    }

    private static (float activity, float peak) ComputeActivity(float peak, double sumSquares, int sampleCount)
    {
        if (sampleCount <= 0)
            return (0f, peak);

        var rms = (float)Math.Sqrt(sumSquares / sampleCount);
        // Weighted blend lowers sensitivity to single-sample spikes.
        var activity = Math.Clamp((rms * 2.2f) + (peak * 0.35f), 0f, 1f);
        return (activity, peak);
    }

    public void Dispose()
    {
        _maxDurationTimer?.Dispose();
        _inputHealthTimer?.Dispose();
        _capture?.Dispose();
        _captureDevice?.Dispose();
        CloseWriter();

        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch { }
        }
        _tempFiles.Clear();

        GC.SuppressFinalize(this);
    }
}
