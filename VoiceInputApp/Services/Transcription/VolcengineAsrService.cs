using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceInputApp.Models;
using VoiceInputApp.Services.Logging;
using VoiceInputApp.Services.Settings;

namespace VoiceInputApp.Services.Transcription;

public class VolcengineAsrService : ITranscriptionService
{
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _logger = LoggingService.Instance;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _webSocket;
    private Action<TranscriptionResult>? _onResult;
    private Task? _receiveTask;
    private TaskCompletionSource<TranscriptionResult?>? _completionSource;
    private int _sequenceNumber;
    private int _audioPacketsSent;
    private string _latestText = string.Empty;
    private string _currentSessionId = string.Empty;
    private volatile bool _isStopping;

    private const string WsUrl = "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel";

    private const byte ProtocolVersion = 0x01;
    private const byte HeaderSize = 0x01;

    private const byte FullClientRequest = 0x01;
    private const byte AudioOnlyRequest = 0x02;
    private const byte FullServerResponse = 0x09;
    private const byte ErrorResponse = 0x0F;

    private const byte NoSequence = 0x00;
    private const byte PosSequence = 0x01;
    private const byte NegSequence = 0x02;
    private const byte NegWithSequence = 0x03;

    private const byte NoSerialization = 0x00;
    private const byte JsonSerialization = 0x01;

    private const byte NoCompression = 0x00;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public bool IsConfigured =>
        !string.IsNullOrEmpty(_settingsService.Current.Asr.AppId) &&
        !string.IsNullOrEmpty(_settingsService.Current.Asr.Token);

    public VolcengineAsrService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task StartStreamingRecognitionAsync(
        Language language,
        string sessionId,
        Action<TranscriptionResult> onResult,
        CancellationToken cancellationToken)
    {
        _onResult = onResult;
        _sequenceNumber = 1;
        _audioPacketsSent = 0;
        _latestText = string.Empty;
        _currentSessionId = sessionId;
        _isStopping = false;
        _completionSource = new TaskCompletionSource<TranscriptionResult?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var settings = _settingsService.Current.Asr;
        if (string.IsNullOrEmpty(settings.AppId) || string.IsNullOrEmpty(settings.Token))
        {
            var errorResult = new TranscriptionResult
            {
                IsError = true,
                ErrorMessage = "ASR 未配置，请填写 AppID 和 Token"
            };

            _completionSource.TrySetResult(errorResult);
            _logger.Error($"[{sessionId}] ASR not configured properly");
            onResult(errorResult);
            throw new InvalidOperationException(errorResult.ErrorMessage);
        }

        _logger.Info($"[{sessionId}] Starting ASR connection. AppId: {settings.AppId}, ResourceId: {settings.ResourceId}");

        try
        {
            await DisposeWebSocketAsync();

            _webSocket = new ClientWebSocket();
            _webSocket.Options.SetRequestHeader("X-Api-App-Key", settings.AppId);
            _webSocket.Options.SetRequestHeader("X-Api-Access-Key", settings.Token);
            _webSocket.Options.SetRequestHeader("X-Api-Resource-Id", settings.ResourceId);
            _webSocket.Options.SetRequestHeader("X-Api-Connect-Id", Guid.NewGuid().ToString());

            _logger.Info($"[{sessionId}] Connecting to {WsUrl}");
            await _webSocket.ConnectAsync(new Uri(WsUrl), cancellationToken);
            _logger.Info($"[{sessionId}] WebSocket connected. State: {_webSocket.State}");

            await SendFullClientRequestAsync(language, cancellationToken);
            _receiveTask = ReceiveResultsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var errorResult = new TranscriptionResult
            {
                IsError = true,
                ErrorMessage = $"ASR 连接失败: {ex.Message}"
            };

            _completionSource.TrySetResult(errorResult);
            _logger.Error($"[{sessionId}] ASR connection failed", ex);
            onResult(errorResult);
            await DisposeWebSocketAsync();
            throw;
        }
    }

    public async Task SendAudioDataAsync(byte[] data, CancellationToken cancellationToken)
    {
        if (_webSocket?.State != WebSocketState.Open || _isStopping)
        {
            _logger.Warning($"[{_currentSessionId}] Cannot send audio: WebSocket state={_webSocket?.State}, isStopping={_isStopping}");
            return;
        }

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            _audioPacketsSent++;

            var header = GenerateHeader(AudioOnlyRequest, NoSequence, NoSerialization, NoCompression);
            var payloadSize = BitConverter.GetBytes((uint)data.Length);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(payloadSize);
            }

            var message = new byte[4 + 4 + data.Length];
            Array.Copy(header, 0, message, 0, 4);
            Array.Copy(payloadSize, 0, message, 4, 4);
            Array.Copy(data, 0, message, 8, data.Length);

            await _webSocket.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Binary, true, cancellationToken);
            if (_audioPacketsSent % 10 == 0)
            {
                _logger.Debug($"[{_currentSessionId}] Sent {_audioPacketsSent} audio packets, last packet size: {data.Length} bytes");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[{_currentSessionId}] SendAudioDataAsync error (packet #{_audioPacketsSent})", ex);
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<TranscriptionResult?> StopRecognitionAsync(CancellationToken cancellationToken)
    {
        if (_completionSource == null)
        {
            return null;
        }

        _logger.Info($"[{_currentSessionId}] Stopping recognition. Total audio packets sent: {_audioPacketsSent}, latestText: '{_latestText}'");
        _isStopping = true;

        if (_webSocket?.State == WebSocketState.Open)
        {
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                var header = GenerateHeader(AudioOnlyRequest, NegSequence, NoSerialization, NoCompression);
                var payloadSize = BitConverter.GetBytes((uint)0);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(payloadSize);
                }

                var message = new byte[8];
                Array.Copy(header, 0, message, 0, 4);
                Array.Copy(payloadSize, 0, message, 4, 4);

                await _webSocket.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Binary, true, cancellationToken);
                _logger.Info($"[{_currentSessionId}] Sent end packet");
            }
            catch (Exception ex)
            {
                _logger.Error($"[{_currentSessionId}] Error sending ASR end packet", ex);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        var finalResult = await WaitForCompletionAsync(_completionSource.Task, _receiveTask, cancellationToken);

        if (_webSocket?.State == WebSocketState.Open || _webSocket?.State == WebSocketState.CloseReceived)
        {
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", cancellationToken);
                _logger.Info($"[{_currentSessionId}] WebSocket closed normally");
            }
            catch (Exception ex)
            {
                _logger.Warning($"[{_currentSessionId}] Failed to close WebSocket cleanly: {ex.Message}");
            }
        }

        await DisposeWebSocketAsync();
        return finalResult;
    }

    private async Task SendFullClientRequestAsync(Language language, CancellationToken cancellationToken)
    {
        var request = new
        {
            user = new
            {
                uid = Environment.MachineName
            },
            audio = new
            {
                format = "pcm",
                codec = "raw",
                rate = 16000,
                bits = 16,
                channel = 1
            },
            request = new
            {
                reqid = Guid.NewGuid().ToString(),
                sequence = _sequenceNumber++,
                nbest = 1
            }
        };

        var jsonPayload = JsonSerializer.Serialize(request);
        _logger.Debug($"[{_currentSessionId}] Sending full client request: {jsonPayload}");

        var payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);
        var header = GenerateHeader(FullClientRequest, NoSequence, JsonSerialization, NoCompression);
        var payloadSize = BitConverter.GetBytes((uint)payloadBytes.Length);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(payloadSize);
        }

        var message = new byte[4 + 4 + payloadBytes.Length];
        Array.Copy(header, 0, message, 0, 4);
        Array.Copy(payloadSize, 0, message, 4, 4);
        Array.Copy(payloadBytes, 0, message, 8, payloadBytes.Length);

        await _webSocket!.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Binary, true, cancellationToken);
        _logger.Info($"[{_currentSessionId}] Full client request sent");
    }

    private async Task ReceiveResultsAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16384];

        try
        {
            while (_webSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.Info($"[{_currentSessionId}] WebSocket closed by server");
                    break;
                }

                if (result.MessageType != WebSocketMessageType.Binary || result.Count < 4)
                {
                    continue;
                }

                var messageType = (byte)(buffer[1] >> 4);
                var messageFlags = (byte)(buffer[1] & 0x0F);
                _logger.Debug($"[{_currentSessionId}] Received messageType={messageType}, flags={messageFlags}, bytes={result.Count}");

                if (messageType == FullServerResponse)
                {
                    HandleFullServerResponse(buffer, result.Count, messageFlags);
                }
                else if (messageType == ErrorResponse)
                {
                    HandleErrorResponse(buffer, result.Count);
                }
                else
                {
                    var hexData = BitConverter.ToString(buffer, 0, Math.Min(result.Count, 32));
                    _logger.Warning($"[{_currentSessionId}] Unknown message format: messageType={messageType}, hex={hexData}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info($"[{_currentSessionId}] Recognition cancelled");
        }
        catch (WebSocketException ex)
        {
            _logger.Error($"[{_currentSessionId}] WebSocket error during recognition", ex);
        }
        catch (Exception ex)
        {
            _logger.Error($"[{_currentSessionId}] Recognition error", ex);
            if (!_isStopping)
            {
                var errorResult = new TranscriptionResult
                {
                    IsError = true,
                    ErrorMessage = $"ASR 识别错误: {ex.Message}"
                };
                _completionSource?.TrySetResult(errorResult);
                _onResult?.Invoke(errorResult);
            }
        }
        finally
        {
            if (_completionSource is { Task: { IsCompleted: false } })
            {
                _completionSource.TrySetResult(string.IsNullOrWhiteSpace(_latestText)
                    ? null
                    : new TranscriptionResult
                    {
                        Text = _latestText,
                        IsFinal = true
                    });
            }
        }
    }

    private void HandleFullServerResponse(byte[] buffer, int count, byte messageFlags)
    {
        var offset = 4;
        if (messageFlags == PosSequence || messageFlags == NegWithSequence)
        {
            offset = 8;
        }

        if (count < offset + 4)
        {
            return;
        }

        var payloadSizeBytes = new byte[4];
        Array.Copy(buffer, offset, payloadSizeBytes, 0, 4);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(payloadSizeBytes);
        }

        var payloadSize = BitConverter.ToUInt32(payloadSizeBytes, 0);
        if (payloadSize == 0 || count < offset + 4 + payloadSize)
        {
            return;
        }

        var payload = new byte[payloadSize];
        Array.Copy(buffer, offset + 4, payload, 0, payloadSize);
        var json = Encoding.UTF8.GetString(payload);
        _logger.Debug($"[{_currentSessionId}] ASR response JSON: {json}");

        try
        {
            var response = JsonSerializer.Deserialize<VolcengineResponse>(json, JsonOptions);
            if (response?.Code != null && response.Code != 1000)
            {
                var errorResult = new TranscriptionResult
                {
                    IsError = true,
                    ErrorMessage = $"ASR 错误 ({response.Code}): {response.Message}"
                };
                _logger.Error($"[{_currentSessionId}] ASR error code: {response.Code}, message: {response.Message}");
                _completionSource?.TrySetResult(errorResult);
                _onResult?.Invoke(errorResult);
                return;
            }

            if (string.IsNullOrWhiteSpace(response?.Result?.Text))
            {
                _logger.Warning($"[{_currentSessionId}] Empty or null text in response");
                return;
            }

            var text = response.Result.Text;
            var isFinal = response.Result.Utterances?.FirstOrDefault()?.Definite ?? false;
            _latestText = text;

            var transcriptionResult = new TranscriptionResult
            {
                Text = text,
                IsFinal = isFinal
            };

            if (isFinal)
            {
                _logger.Info($"[{_currentSessionId}] Final recognition result: '{text}'");
            }
            else
            {
                _logger.Debug($"[{_currentSessionId}] Partial recognition result: '{text}'");
            }
            _onResult?.Invoke(transcriptionResult);

            if (isFinal)
            {
                _completionSource?.TrySetResult(transcriptionResult);
            }
        }
        catch (JsonException ex)
        {
            _logger.Error($"[{_currentSessionId}] JSON parse error", ex);
        }
    }

    private void HandleErrorResponse(byte[] buffer, int count)
    {
        if (count < 12)
        {
            return;
        }

        var errorCodeBytes = new byte[4];
        Array.Copy(buffer, 4, errorCodeBytes, 0, 4);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(errorCodeBytes);
        }

        var errorCode = BitConverter.ToInt32(errorCodeBytes, 0);

        var errorMsgSizeBytes = new byte[4];
        Array.Copy(buffer, 8, errorMsgSizeBytes, 0, 4);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(errorMsgSizeBytes);
        }

        var errorMsgSize = BitConverter.ToInt32(errorMsgSizeBytes, 0);
        var errorMsg = errorMsgSize > 0 && count >= 12 + errorMsgSize
            ? Encoding.UTF8.GetString(buffer, 12, errorMsgSize)
            : "Unknown error";

        var errorResult = new TranscriptionResult
        {
            IsError = true,
            ErrorMessage = $"ASR 错误 ({errorCode}): {errorMsg}"
        };

        _logger.Error($"[{_currentSessionId}] ASR error response: code={errorCode}, message={errorMsg}");
        _completionSource?.TrySetResult(errorResult);
        _onResult?.Invoke(errorResult);
    }

    private async Task<TranscriptionResult?> WaitForCompletionAsync(
        Task<TranscriptionResult?> completionTask,
        Task? receiveTask,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(4));
        var waitTasks = new List<Task> { completionTask };
        if (receiveTask != null)
        {
            waitTasks.Add(receiveTask);
        }

        waitTasks.Add(Task.Delay(Timeout.InfiniteTimeSpan, timeoutCts.Token));
        var finishedTask = await Task.WhenAny(waitTasks);

        if (finishedTask == completionTask)
        {
            timeoutCts.Cancel();
            return await completionTask;
        }

        if (receiveTask != null && finishedTask == receiveTask)
        {
            timeoutCts.Cancel();
            await receiveTask;
            return completionTask.IsCompleted ? await completionTask : null;
        }

        _logger.Warning($"[{_currentSessionId}] Timed out waiting for final ASR result");
        return completionTask.IsCompleted ? await completionTask : null;
    }

    private async Task DisposeWebSocketAsync()
    {
        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask;
            }
            catch
            {
            }
        }

        _webSocket?.Dispose();
        _webSocket = null;
        _receiveTask = null;
    }

    private static byte[] GenerateHeader(byte messageType, byte messageFlags, byte serialization, byte compression)
    {
        return new byte[]
        {
            (byte)((ProtocolVersion << 4) | HeaderSize),
            (byte)((messageType << 4) | messageFlags),
            (byte)((serialization << 4) | compression),
            0x00
        };
    }

    private class VolcengineResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("result")]
        public VolcengineResult? Result { get; set; }
    }

    private class VolcengineResult
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("utterances")]
        public List<Utterance>? Utterances { get; set; }
    }

    private class Utterance
    {
        [JsonPropertyName("definite")]
        public bool Definite { get; set; }
    }
}
