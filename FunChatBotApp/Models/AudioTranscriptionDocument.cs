using Newtonsoft.Json;
using System;

namespace FunChatBotApp.Models;

public class AudioTranscriptionDocument : WorkspaceDocument
{
    public AudioTranscriptionDocument()
    {
        Type = "AudioTranscription";
    }

    [JsonProperty("originalFileName")]
    public string OriginalFileName { get; set; } = string.Empty;

    [JsonProperty("blobUrl")]
    public string BlobUrl { get; set; } = string.Empty;

    [JsonProperty("transcriptText")]
    public string TranscriptText { get; set; } = string.Empty;

    [JsonProperty("status")]
    public TranscriptionState Status { get; set; } = TranscriptionState.Uploading;

    [JsonProperty("apiTranscriptionUri")]
    public string ApiTranscriptionUri { get; set; } = string.Empty;

    [JsonProperty("errorMessage")]
    public string ErrorMessage { get; set; } = string.Empty;

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
