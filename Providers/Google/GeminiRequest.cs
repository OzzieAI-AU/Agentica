namespace OzzieAI.Agentica.Providers.Google
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    internal class GeminiRequest
    {
        [JsonPropertyName("contents")] public List<Content> Contents { get; set; } = new();
        [JsonPropertyName("tools")] public List<Tool>? Tools { get; set; }
        [JsonPropertyName("systemInstruction")] public Content? SystemInstruction { get; set; }
    }

    internal class Content
    {
        [JsonPropertyName("role")] public string? Role { get; set; }
        [JsonPropertyName("parts")] public List<Part> Parts { get; set; } = new();
    }

    internal class Part
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("inline_data")] public InlineData? InlineData { get; set; }
        [JsonPropertyName("functionCall")] public FunctionCall? FunctionCall { get; set; }
        [JsonPropertyName("functionResponse")] public FunctionResponse? FunctionResponse { get; set; }
    }

    internal class InlineData
    {
        [JsonPropertyName("mime_type")] public string MimeType { get; set; } = "";
        [JsonPropertyName("data")] public string Base64Data { get; set; } = "";
    }

    internal class FunctionResponse
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("response")] public object Response { get; set; } = new { };
    }

    internal class Tool { [JsonPropertyName("function_declarations")] public List<object> FunctionDeclarations { get; set; } = new(); }
    internal class FunctionCall { [JsonPropertyName("name")] public string Name { get; set; } = ""; [JsonPropertyName("args")] public object? Args { get; set; } }

    internal class GeminiResponse
    {
        [JsonPropertyName("candidates")] public List<Candidate>? Candidates { get; set; }
    }

    internal class Candidate { [JsonPropertyName("content")] public Content? Content { get; set; } }
}