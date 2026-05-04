using System.IO;
using NAudio.Wave;
using VoiceInputApp.Services.Logging;

namespace VoiceInputApp.Services.Audio;

public class AudioPlaybackService : IAudioPlaybackService, IDisposable
{
    private readonly ILoggingService _logger = LoggingService.Instance;
    private readonly object _lock = new();
    private WaveOutEvent? _waveOut;
    private WaveStream? _reader;
    private string? _tempFilePath;
    private bool _isStopping;

    public event EventHandler<PlaybackCompletedEventArgs>? PlaybackCompleted;

    public bool IsPlaying
    {
        get
        {
            lock (_lock)
            {
                return _waveOut?.PlaybackState == PlaybackState.Playing;
            }
        }
    }

    public async Task PlayAsync(byte[] audioData, string mimeType, CancellationToken cancellationToken)
    {
        if (audioData == null || audioData.Length == 0)
        {
            throw new InvalidOperationException("没有可播放的音频数据");
        }

        Stop();

        var extension = GetExtension(mimeType);
        var tempFile = Path.Combine(Path.GetTempPath(), $"voiceinput_reply_{Guid.NewGuid():N}{extension}");

        try
        {
            await File.WriteAllBytesAsync(tempFile, audioData, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to write temp audio file: {ex.Message}", ex);
            throw;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _isStopping = false;
            _tempFilePath = tempFile;

            try
            {
                _reader = new AudioFileReader(tempFile);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to create audio reader: {ex.Message}", ex);
                TryDeleteFile(tempFile);
                _tempFilePath = null;
                throw new InvalidOperationException($"音频文件格式错误: {ex.Message}", ex);
            }

            _waveOut = new WaveOutEvent();
            _waveOut.PlaybackStopped += (_, e) =>
            {
                if (e.Exception != null)
                {
                    _logger.Error($"Playback error: {e.Exception.Message}", e.Exception);
                    completion.TrySetException(e.Exception);
                    PlaybackCompleted?.Invoke(this, new PlaybackCompletedEventArgs(false, e.Exception.Message));
                }
                else
                {
                    completion.TrySetResult();
                    PlaybackCompleted?.Invoke(this, new PlaybackCompletedEventArgs(true, null));
                }
            };

            try
            {
                _waveOut.Init(_reader);
                _waveOut.Play();
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to start playback: {ex.Message}", ex);
                CleanupPlayback();
                throw new InvalidOperationException($"播放启动失败: {ex.Message}", ex);
            }
        }

        using var registration = cancellationToken.Register(() =>
        {
            _logger.Info("Playback cancelled via cancellation token");
            Stop();
        });

        try
        {
            await completion.Task;
        }
        catch (Exception ex)
        {
            _logger.Error($"Playback failed: {ex.Message}", ex);
            throw;
        }
        finally
        {
            CleanupPlayback();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_isStopping) return;
            _isStopping = true;

            try
            {
                _waveOut?.Stop();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to stop playback: {ex.Message}");
            }
        }

        CleanupPlayback();
    }

    private void CleanupPlayback()
    {
        string? tempFileToDelete = null;
        lock (_lock)
        {
            _waveOut?.Dispose();
            _reader?.Dispose();
            _waveOut = null;
            _reader = null;
            tempFileToDelete = _tempFilePath;
            _tempFilePath = null;
            _isStopping = false;
        }

        TryDeleteFile(tempFileToDelete);
    }

    private void TryDeleteFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                File.Delete(filePath);
                return;
            }
            catch (IOException)
            {
                if (attempt < 2)
                {
                    System.Threading.Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to delete temp file {filePath}: {ex.Message}");
                return;
            }
        }

        _logger.Warning($"Could not delete temp file after 3 attempts: {filePath}");
    }

    private static string GetExtension(string mimeType)
    {
        return mimeType.Contains("wav", StringComparison.OrdinalIgnoreCase) ? ".wav" : ".mp3";
    }

    public void Dispose()
    {
        Stop();
    }
}

public class PlaybackCompletedEventArgs : EventArgs
{
    public bool Success { get; }
    public string? ErrorMessage { get; }

    public PlaybackCompletedEventArgs(bool success, string? errorMessage)
    {
        Success = success;
        ErrorMessage = errorMessage;
    }
}
