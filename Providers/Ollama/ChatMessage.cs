namespace OzzieAI.Agentica.Providers.Ollama
{

    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The universal message format for the Agentica framework.
    /// Supports Text, Tool Results, and Multimodal (Images/Video/etc).
    /// </summary>
    public class ChatMessage : IChatMessage
    {

        /// <summary>
        /// The identity of the message author. 
        /// Common values include "system", "user", "assistant", and "tool".
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// The primary text content of the message. 
        /// For 'tool' roles, this typically contains the stringified result of the execution.
        /// </summary>
        public object Content { get; set; }

        /// <summary>
        /// Base64 encoded binary data for multimodal processing. 
        /// Used for injecting images, audio, or video frames into the conversation context.
        /// </summary>
        public string? MediaData { get; set; }

        /// <summary>
        /// The IANA standard Media Type (e.g., "image/jpeg", "video/mp4") 
        /// describing the format of the <see cref="MediaData"/>.
        /// </summary>
        public string? MimeType { get; set; }

        /// <summary>
        /// The unique identifier for a specific tool execution request. 
        /// Essential for Grok and OpenAI providers to link function results back to the original call.
        /// </summary>
        public string? ToolId { get; set; }

        /// <summary>
        /// The human-readable name of the tool associated with this message.
        /// </summary>
        public string? ToolName { get; set; }


        public ChatMessage() { }

        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }

    /// <summary>
    /// Ollama-specific JSON Request DTO.
    /// </summary>
    internal class OllamaChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public List<OllamaMessage> Messages { get; set; } = new();
        [JsonPropertyName("stream")] public bool Stream { get; set; } = false;
        [JsonPropertyName("tools")] public List<object>? Tools { get; set; }
        [JsonPropertyName("options")] public Dictionary<string, object>? Options { get; set; }
    }

    internal class OllamaMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";

        [JsonPropertyName("images")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Images { get; set; }

        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<OllamaToolCall>? ToolCalls { get; set; }
    }

    internal class OllamaToolCall
    {
        [JsonPropertyName("function")] public OllamaFunction Function { get; set; } = new();
    }

    internal class OllamaFunction
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("arguments")] public Dictionary<string, object>? Arguments { get; set; }
    }
}