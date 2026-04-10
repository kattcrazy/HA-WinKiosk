namespace HAWinKiosk.Wyoming;

/// <summary>
/// Host UI for voice assist: Wyoming events are raised from background threads;
/// implementations must marshal to the UI thread.
/// </summary>
public interface IVoiceAssistUiHost
{
    /// <summary>Wake word accepted; HA pipeline started — show line + glow.</summary>
    void VoiceAssistSessionStarted();

    /// <summary>Streaming STT partial text (above the divider line).</summary>
    void VoiceTranscriptPartial(string? text);

    /// <summary>Final user utterance (locked above the line).</summary>
    void VoiceTranscriptFinal(string? text);

    /// <summary>Between final STT and assistant reply (optional visual).</summary>
    void VoiceProcessing();

    /// <summary>Clear assistant reply area before streaming TTS text.</summary>
    void VoiceAssistantReplyClear();

    /// <summary>Append a fragment of the assistant reply (below the line), e.g. from synthesize-chunk.</summary>
    void VoiceAssistantReplyAppend(string? chunk);

    /// <summary>TTS audio stream starting (playback).</summary>
    void VoiceTtsPlaybackStarted();

    /// <summary>Interaction finished — fade text, hide line and glow.</summary>
    void VoiceAssistSessionEnded();
}
