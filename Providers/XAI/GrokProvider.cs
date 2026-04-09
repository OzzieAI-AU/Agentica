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

    /// <summary>
    /// Provides integration with xAI's Grok API (api.x.ai) as an implementation of <see cref="ILlmProvider"/>.
    /// Supports text messages, multimodal images (base64), tool calling, and tool result handling.
    /// </summary>
    public class GrokProvider : ILlmProvider
    {
        private readonly string _apiKey;
        private readonly string _model;
        private readonly HttpClient _http;

        /// <summary>
        /// The base endpoint for xAI Chat Completions API.
        /// </summary>
        private const string BaseUrl = "https://api.x.ai/v1/chat/completions";

        /// <summary>
        /// JSON serialization options used for all requests and responses.
        /// Uses camelCase naming policy and omits null properties.
        /// </summary>
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="GrokProvider"/> class.
        /// </summary>
        /// <param name="apiKey">The xAI API key used for authentication. Must not be null.</param>
        /// <param name="model">The Grok model to use. Defaults to "grok-4-1-fast-reasoning".</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="apiKey"/> is null.</exception>
        public GrokProvider(string apiKey, string model = "grok-4-1-fast-reasoning")
        {
            // Validate and store API key
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));

            // Store model name
            _model = model;

            // Create HttpClient with generous timeout for complex reasoning
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

            // Set Bearer token authorization for all outgoing requests
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        /// <summary>
        /// Generates a response from the Grok model using the provided conversation history and optional tools.
        /// </summary>
        /// <param name="history">The conversation history as a list of chat messages.</param>
        /// <param name="tools">Optional list of tools available for the model to call.</param>
        /// <returns>An <see cref="IChatResponse"/> containing the model's response and any tool calls.</returns>
        public async Task<IChatResponse> GenerateResponseAsync(List<IChatMessage> history, List<IAgentTool>? tools = null)
        {
            // Create the request object with the selected model
            var request = new GrokRequest { Model = _model };

            // Process each message in the conversation history
            foreach (var msg in history)
            {
                // Cast to the framework's concrete ChatMessage type (not the internal DTO)
                var chatMsg = (ChatMessage)msg;

                // Create Grok-compatible message with normalized role (lowercase)
                var grokMsg = new GrokMessage { Role = chatMsg.Role.ToLower() };

                // 1. Handle Tool Results (mapped to 'tool' role in xAI API)
                if (chatMsg.Role.Equals("tool", StringComparison.OrdinalIgnoreCase))
                {
                    // Tool call result - attach tool identifier
                    grokMsg.ToolId = chatMsg.ToolName; // Consider using a dedicated ToolCallId property in future
                    grokMsg.Content = chatMsg.Content;
                }
                // 2. Handle Multimodal Messages (Images)
                else if (!string.IsNullOrEmpty(chatMsg.MediaData))
                {
                    var parts = new List<GrokContentPart>();

                    // Add text content if present alongside the image
                    if (!string.IsNullOrEmpty(chatMsg.Content?.ToString()))
                        parts.Add(new GrokContentPart { Type = "text", Text = chatMsg.Content.ToString() });

                    // Add image as base64 data URL
                    parts.Add(new GrokContentPart
                    {
                        Type = "image_url",
                        ImageUrl = new GrokImageUrl
                        {
                            Url = $"data:{chatMsg.MimeType ?? "image/jpeg"};base64,{chatMsg.MediaData}"
                        }
                    });

                    grokMsg.Content = parts;
                }
                // 3. Standard Text-Only Message
                else
                {
                    grokMsg.Content = chatMsg.Content;
                }

                // Add the processed message to the request
                request.Messages.Add(grokMsg);
            }

            // 4. Add tool definitions if any tools are provided
            if (tools != null && tools.Any())
            {
                request.Tools = tools.Select(t => t.GetToolDefinition()).ToList();
            }

            // 5. Serialize and send request to xAI API
            var jsonBody = JsonSerializer.Serialize(request, _jsonOptions);
            var response = await _http.PostAsync(BaseUrl, new StringContent(jsonBody, Encoding.UTF8, "application/json"));
            var responseJson = await response.Content.ReadAsStringAsync();

            // Handle API errors gracefully
            if (!response.IsSuccessStatusCode)
                return new LlmResponse { Content = $"Grok API Error: {response.StatusCode} | {responseJson}" };

            // 6. Parse the successful response
            var grokResult = JsonSerializer.Deserialize<GrokResponse>(responseJson, _jsonOptions);
            var choice = grokResult?.Choices?.FirstOrDefault()?.Message;

            if (choice == null)
                return new LlmResponse { Content = "Error: Grok returned no response." };

            var result = new LlmResponse();

            // Handle content that might be string or complex JsonElement (for multimodal responses)
            if (choice.Content is JsonElement element)
            {
                result.Content = element.ValueKind == JsonValueKind.String
                    ? element.GetString() ?? ""
                    : element.GetRawText();
            }
            else
            {
                result.Content = choice.Content?.ToString() ?? "";
            }

            // Extract any tool calls made by the model
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

        /// <summary>
        /// Checks the health of the Grok API by calling the models endpoint.
        /// </summary>
        /// <returns><c>true</c> if the API is reachable and returns success status; otherwise <c>false</c>.</returns>
        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                // Simple GET request to /v1/models to verify API key and connectivity
                var response = await _http.GetAsync("https://api.x.ai/v1/models");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                // Any exception (network, timeout, auth, etc.) means unhealthy
                return false;
            }
        }
    }
}