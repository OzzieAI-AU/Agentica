namespace OzzieAI.Agentica.Providers.Xai
{
    using OzzieAI.Agentica.Providers.Google;
    using OzzieAI.Agentica.Tools;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;

    public class GrokProvider : ILlmProvider
    {

        private readonly string _apiKey;
        private readonly string _model;
        private readonly HttpClient _http;
        private const string BaseUrl = "https://api.x.ai/v1/chat/completions";


        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };


        public GrokProvider(string apiKey, string model = "grok-2-vision-latest")
        {

            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _model = model;
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }


        public async Task<IChatResponse> GenerateResponseAsync(List<IChatMessage> history, List<IAgentTool>? tools = null)
        {
        
            var request = new GrokRequest { Model = _model };

            foreach (var msg in history)
            {
                // Fix: Cast to the framework's ChatMessage, NOT the internal GrokMessage DTO
                var chatMsg = (ChatMessage)msg;
                var grokMsg = new GrokMessage { Role = chatMsg.Role.ToLower() };

                // 1. Handle Tool Results (mapped to 'tool' role in xAI)
                if (chatMsg.Role.Equals("tool", StringComparison.OrdinalIgnoreCase))
                {
                    grokMsg.ToolId = chatMsg.ToolName; // Or specific ToolCallId if you added it to ChatMessage
                    grokMsg.Content = chatMsg.Content;
                }
                // 2. Handle Multimodal (Images)
                else if (!string.IsNullOrEmpty(chatMsg.MediaData))
                {
                    var parts = new List<GrokContentPart>();

                    if (!string.IsNullOrEmpty(chatMsg.Content.ToString()))
                        parts.Add(new GrokContentPart { Type = "text", Text = chatMsg.Content.ToString() });

                    parts.Add(new GrokContentPart
                    {
                        Type = "image_url",
                        ImageUrl = new GrokImageUrl { Url = $"data:{chatMsg.MimeType ?? "image/jpeg"};base64,{chatMsg.MediaData}" }
                    });

                    grokMsg.Content = parts;
                }
                // 3. Standard Text
                else
                {
                    grokMsg.Content = chatMsg.Content;
                }

                request.Messages.Add(grokMsg);
            }

            // 4. Tool Definitions
            if (tools != null && tools.Any())
            {
                request.Tools = tools.Select(t => t.GetToolDefinition()).ToList();
            }

            // 5. Execution
            var jsonBody = JsonSerializer.Serialize(request, _jsonOptions);
            var response = await _http.PostAsync(BaseUrl, new StringContent(jsonBody, Encoding.UTF8, "application/json"));
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return new LlmResponse { Content = $"Grok API Error: {response.StatusCode} | {responseJson}" };

            // 6. Response Parsing
            var grokResult = JsonSerializer.Deserialize<GrokResponse>(responseJson, _jsonOptions);
            var choice = grokResult?.Choices?.FirstOrDefault()?.Message;

            if (choice == null) return new LlmResponse { Content = "Error: Grok returned no response." };

            var result = new LlmResponse();

            // Handle content which might be a string or a list of parts in the response
            if (choice.Content is JsonElement element)
            {
                result.Content = element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : element.GetRawText();
            }
            else
            {
                result.Content = choice.Content?.ToString() ?? "";
            }

            if (choice.ToolCalls != null && choice.ToolCalls.Any())
            {
                result.ToolCalls = choice.ToolCalls.Select(tc => new ToolCall
                {
                    Id = tc.Id,
                    ToolName = tc.Function.Name,
                    ArgumentsJson = tc.Function.Arguments
                }).ToList();
            }

            return result;
        }

        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                var response = await _http.GetAsync("https://api.x.ai/v1/models");
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }
    }
}