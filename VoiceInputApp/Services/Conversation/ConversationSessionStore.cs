using System.IO;
using System.Text.Json;
using VoiceInputApp.Models;
using VoiceInputApp.Services.Settings;

namespace VoiceInputApp.Services.Conversation;

public class ConversationSessionStore : IConversationSessionStore
{
    private readonly ISettingsService _settingsService;
    private readonly List<ConversationMessage> _messages = new();
    private readonly object _lock = new();
    private readonly string _sessionFilePath;
    private bool _dirty;

    public ConversationSessionStore(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var sessionDir = Path.Combine(appDataPath, "VoiceInput");
        Directory.CreateDirectory(sessionDir);
        _sessionFilePath = Path.Combine(sessionDir, "conversation_session.json");
        LoadFromDisk();
    }

    public IReadOnlyList<ConversationMessage> GetMessages()
    {
        lock (_lock)
        {
            return _messages.ToList();
        }
    }

    public void AddUserMessage(string text)
    {
        AddMessage("user", text);
    }

    public void AddAssistantMessage(string text)
    {
        AddMessage("assistant", text);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _messages.Clear();
            _dirty = true;
        }

        SaveToDisk();
    }

    public void SaveToDisk()
    {
        lock (_lock)
        {
            if (!_dirty) return;

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_messages, options);
                var tempPath = _sessionFilePath + ".tmp";
                File.WriteAllText(tempPath, json);

                if (File.Exists(_sessionFilePath))
                {
                    File.Replace(tempPath, _sessionFilePath, _sessionFilePath + ".bak");
                }
                else
                {
                    File.Move(tempPath, _sessionFilePath);
                }

                _dirty = false;
            }
            catch
            {
            }
        }
    }

    private void LoadFromDisk()
    {
        lock (_lock)
        {
            if (!File.Exists(_sessionFilePath)) return;

            try
            {
                var json = File.ReadAllText(_sessionFilePath);
                var loaded = JsonSerializer.Deserialize<List<ConversationMessage>>(json);
                if (loaded != null)
                {
                    _messages.Clear();
                    _messages.AddRange(loaded);
                }
            }
            catch
            {
            }
        }
    }

    private void AddMessage(string role, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lock (_lock)
        {
            _messages.Add(new ConversationMessage
            {
                Role = role,
                Content = text.Trim(),
                Timestamp = DateTime.Now
            });

            var maxTurns = Math.Max(1, _settingsService.Current.MaxConversationTurns);
            var maxMessages = maxTurns * 2;
            while (_messages.Count > maxMessages)
            {
                _messages.RemoveAt(0);
            }

            _dirty = true;
        }

        SaveToDisk();
    }
}
