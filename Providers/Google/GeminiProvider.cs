namespace OzzieAI.Agentica.Providers.Google
{
    using OzzieAI.Agentica.Tools;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;

    public class GeminiProvider : ILlmProvider
    {
        private readonly string _apiKey;
        private readonly HttpClient _http;
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-pro:generateContent";
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };


        public GeminiProvider(string apiKey)
        {

            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) }; // Increased for video/audio processing
        }


        public async Task<IChatResponse> GenerateResponseAsync(List<IChatMessage> history, List<IAgentTool>? tools = null)
        {

            var request = new GeminiRequest();

            // 1. System Instruction Extraction
            var systemMsg = history.FirstOrDefault(m => ((ChatMessage)m).Role.Equals("system", StringComparison.OrdinalIgnoreCase));
            if (systemMsg != null)
            {
                request.SystemInstruction = new Content { Parts = new List<Part> { new Part { Text = ((ChatMessage)systemMsg).Content.ToString() } } };
            }

            // 2. Multimodal History Mapping
            foreach (var msg in history.Where(m => !((ChatMessage)m).Role.Equals("system", StringComparison.OrdinalIgnoreCase)))
            {
                var content = new Content { Role = MapRole(((ChatMessage)msg).Role) };

                // Handle Tool Responses (FunctionResponse)
                if (((ChatMessage)msg).Role.Equals("tool", StringComparison.OrdinalIgnoreCase))
                {
                    content.Parts.Add(new Part
                    {
                        FunctionResponse = new FunctionResponse
                        {
                            Name = ((ChatMessage)msg).ToolName ?? "unknown_tool",
                            Response = new { result = ((ChatMessage)msg).Content }
                        }
                    });
                }
                // Handle Multimodal or Standard Text
                else if (((ChatMessage)msg).MediaData != null)
                {
                    content.Parts.Add(new Part { Text = ((ChatMessage)msg).Content.ToString() });
                    content.Parts.Add(new Part
                    {
                        InlineData = new InlineData { MimeType = ((ChatMessage)msg).MimeType ?? "image/jpeg", Base64Data = ((ChatMessage)msg).MediaData }
                    });
                }
                else
                {
                    content.Parts.Add(new Part { Text = ((ChatMessage)msg).Content.ToString() });
                }

                request.Contents.Add(content);
            }

            // 3. Tool Registration
            if (tools != null && tools.Any())
            {
                request.Tools = new List<Tool> {
                    new Tool { FunctionDeclarations = tools.Select(t => t.GetToolDefinition()).ToList() }
                };
            }

            // 4. API Execution
            string url = $"{BaseUrl}?key={_apiKey}";
            string jsonBody = JsonSerializer.Serialize(request, _jsonOptions);
            var httpContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(url, httpContent);
            string responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return new LlmResponse { Content = $"Gemini API Error: {response.StatusCode} | {responseJson}" };

            // 5. Response Parsing (Extracting Text + Tool Calls)
            var geminiResult = JsonSerializer.Deserialize<GeminiResponse>(responseJson, _jsonOptions);
            var candidateContent = geminiResult?.Candidates?.FirstOrDefault()?.Content;

            if (candidateContent == null) return new LlmResponse { Content = "Error: No response from Gemini." };

            var result = new LlmResponse();
            var toolCalls = new List<ToolCall>();

            foreach (var part in candidateContent.Parts)
            {
                if (!string.IsNullOrEmpty(part.Text)) result.Content += part.Text;
                if (part.FunctionCall != null)
                {
                    toolCalls.Add(new ToolCall
                    {
                        ToolName = part.FunctionCall.Name,
                        ArgumentsJson = JsonSerializer.Serialize(part.FunctionCall.Args)
                    });
                }
            }

            result.ToolCalls = toolCalls.Any() ? toolCalls : null;
            return result;
        }

        private string MapRole(string frameworkRole) => frameworkRole.ToLower() switch
        {
            "assistant" => "model",
            "tool" => "function", // Gemini specifically expects 'function' role for tool results
            _ => "user"
        };

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                var response = await _http.GetAsync($"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-pro?key={_apiKey}");
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }
    }
}