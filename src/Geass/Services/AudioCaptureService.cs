using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Geass.Services;

public class AudioCaptureService : IDisposable
{
    private static readonly TimeSpan MaxRecordingDuration = TimeSpan.FromMinutes(5);

    private WasapiCapture? _capture;
    private WaveFileWriter? _writer;
    private string? _rawFilePath;
    private string? _outputFilePath;
    private System.Threading.Timer? _maxDurationTimer;
    private readonly List<string> _tempFiles = [];

    public event Action? RecordingStopped;

    public void StartRecording()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Geass");
        Directory.CreateDirectory(tempDir);

        var id = Guid.NewGuid().ToString("N")[..8];
        _rawFilePath = Path.Combine(tempDir, $"{id}_raw.wav");
        _outputFilePath = Path.Combine(tempDir, $"{id}.wav");
        _tempFiles.Add(_rawFilePath);
        _tempFiles.Add(_outputFilePath);

        _capture = new WasapiCapture();
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnCaptureRecordingStopped;

        // Record in native WASAPI format (typically 32-bit float, 48kHz)
        // No per-buffer resampling - convert after recording completes
        _writer = new WaveFileWriter(_rawFilePath, _capture.WaveFormat);

        _capture.StartRecording();

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

        // Write raw bytes in native format - no conversion needed
        _writer.Write(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnCaptureRecordingStopped(object? sender, StoppedEventArgs e)
    {
        CloseWriter();
        RecordingStopped?.Invoke();
    }

    private void CloseWriter()
    {
        _writer?.Dispose();
        _writer = null;
    }

    public void Dispose()
    {
        _maxDurationTimer?.Dispose();
        _capture?.Dispose();
        CloseWriter();

        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch { }
        }
        _tempFiles.Clear();

        GC.SuppressFinalize(this);
    }
}
