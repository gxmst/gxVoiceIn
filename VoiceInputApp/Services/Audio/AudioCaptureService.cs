using NAudio.Wave;
using VoiceInputApp.Services.Logging;

namespace VoiceInputApp.Services.Audio;

public class AudioCaptureService : IAudioCaptureService
{
    private readonly ILoggingService _logger = LoggingService.Instance;
    private WaveInEvent? _waveIn;
    private readonly AudioLevelCalculator _levelCalculator;
    private bool _isCapturing;
    private int _deviceNumber = -1;
    private int _totalBytesCaptured;

    public event EventHandler<AudioLevelEventArgs>? AudioLevelUpdated;
    public event EventHandler<byte[]>? AudioDataAvailable;

    public bool IsCapturing => _isCapturing;

    public AudioCaptureService()
    {
        _levelCalculator = new AudioLevelCalculator();
    }

    public static int DeviceCount => WaveInEvent.DeviceCount;

    public static List<(int Index, string Name)> GetAvailableDevices()
    {
        var devices = new List<(int, string)>();
        var deviceCount = WaveInEvent.DeviceCount;
        
        for (var i = 0; i < deviceCount; i++)
        {
            var capabilities = WaveInEvent.GetCapabilities(i);
            devices.Add((i, capabilities.ProductName));
        }
        
        return devices;
    }

    public void SetDevice(int deviceNumber)
    {
        if (_isCapturing)
        {
            throw new InvalidOperationException("Cannot change device while capturing");
        }
        _deviceNumber = deviceNumber;
        _logger.Info($"Microphone device set to: {deviceNumber}");
    }

    public void StartCapture()
    {
        if (_isCapturing) return;

        _totalBytesCaptured = 0;

        if (_deviceNumber < 0 || _deviceNumber >= WaveInEvent.DeviceCount)
        {
            _deviceNumber = 0;
            _logger.Info($"Using default microphone device: 0");
        }

        if (WaveInEvent.DeviceCount == 0)
        {
            _logger.Error("No microphone device available");
            throw new InvalidOperationException("No microphone device available");
        }

        var deviceName = WaveInEvent.GetCapabilities(_deviceNumber).ProductName;
        _logger.Info($"Starting capture with device { _deviceNumber}: {deviceName}");

        try
        {
            _waveIn = new WaveInEvent
            {
                DeviceNumber = _deviceNumber,
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 100
            };

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.StartRecording();
            _isCapturing = true;
            _logger.Info("Audio capture started successfully");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to start audio capture", ex);
            _waveIn?.Dispose();
            _waveIn = null;
            throw;
        }
    }

    public void StopCapture()
    {
        if (!_isCapturing) return;

        _isCapturing = false;
        var waveInToStop = Interlocked.Exchange(ref _waveIn, null);

        if (waveInToStop != null)
        {
            _logger.Info($"Stopping capture. Total bytes captured: {_totalBytesCaptured}");
            waveInToStop.StopRecording();
        }
    }

    public float GetCurrentLevel()
    {
        return _levelCalculator.CurrentLevel;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded > 0)
        {
            _totalBytesCaptured += e.BytesRecorded;
            
            var level = _levelCalculator.CalculateLevel(e.Buffer, e.BytesRecorded);
            AudioLevelUpdated?.Invoke(this, new AudioLevelEventArgs { Level = level });

            var audioData = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, audioData, e.BytesRecorded);
            AudioDataAvailable?.Invoke(this, audioData);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _logger.Info("Audio capture stopped");
        if (e.Exception != null)
        {
            _logger.Error("Recording stopped with error", e.Exception);
        }

        var waveInToDispose = Interlocked.Exchange(ref _waveIn, null);
        waveInToDispose?.Dispose();
        _isCapturing = false;
    }
}
