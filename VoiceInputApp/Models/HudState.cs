﻿namespace VoiceInputApp.Models;

public enum HudState
{
    Hidden,
    Listening,
    Transcribing,
    Refining,
    Thinking,
    Synthesizing,
    Speaking,
    Interrupted,
    Success,
    Error
}
