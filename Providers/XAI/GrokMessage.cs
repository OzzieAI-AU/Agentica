namespace OzzieAI.Agentica.Providers.Xai
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    internal class GrokRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "grok-2-vision-latest";
        [JsonPropertyName("messages")] public List<GrokMessage> Messages { get; set; } = new();
        [JsonPropertyName("stream")] public bool Stream { get; set; } = false;
        [JsonPropertyName("tools")] public List<object>? Tools { get; set; }
    }

    internal class GrokMessage : IChatMessage
    {

        /// <summary>
        /// The identity of the message author. 
        /// Common values include "system", "user", "assistant", and "tool".
        /// </summary>
        [JsonPropertyName("role")]
        public string Role { get; set; }

        /// <summary>
        /// The primary text content of the message. 
        /// For 'tool' roles, this typically contains the stringified result of the execution.
        /// </summary>
        [JsonPropertyName("content")]
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
        [JsonPropertyName("tool_call_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolId { get; set; }

        /// <summary>
        /// The human-readable name of the tool associated with this message.
        /// </summary>
        public string? ToolName { get; set; }


        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<GrokToolCall>? ToolCalls { get; set; }
    }

    internal class GrokContentPart
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("text")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Text { get; set; }
        [JsonPropertyName("image_url")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GrokImageUrl? ImageUrl { get; set; }
    }

    internal class GrokImageUrl { [JsonPropertyName("url")] public string Url { get; set; } = ""; }

    internal class GrokToolCall
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "function";
        [JsonPropertyName("function")] public GrokFunction Function { get; set; } = new();
    }

    internal class GrokFunction
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("arguments")] public string Arguments { get; set; } = "";
    }

    internal class GrokResponse
    {
        [JsonPropertyName("choices")] public List<GrokChoice>? Choices { get; set; }
    }

    internal class GrokChoice { [JsonPropertyName("message")] public GrokMessage? Message { get; set; } }
}