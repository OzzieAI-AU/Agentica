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

    /// <summary>
    /// Provides integration with Google's Gemini LLM API (e.g., gemini-1.5-pro, gemini-1.5-flash).
    /// Handles multimodal inputs (text + images), system instructions, and function/tool calling.
    /// </summary>
    public class GeminiProvider : ILlmProvider
    {
        private readonly string _apiKey;
        private readonly string _model;
        private readonly HttpClient _http;

        /// <summary>
        /// Base URI for the Google Generative AI (Gemini) REST API.
        /// </summary>
        private const string ApiBaseUri = "https://generativelanguage.googleapis.com/v1beta/models";

        /// <summary>
        /// JSON serializer options configured for Google's Gemini API requirements.
        /// Uses camelCase naming and gracefully handles null values and case-insensitive deserialization.
        /// </summary>
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="GeminiProvider"/> class.
        /// </summary>
        /// <param name="apiKey">Your Google AI Studio / Gemini API key.</param>
        /// <param name="model">The Gemini model identifier (e.g. "gemini-1.5-pro" or "gemini-1.5-flash").</param>
        /// <exception cref="ArgumentNullException">Thrown when the API key is null or whitespace.</exception>
        public GeminiProvider(string apiKey, string model = "gemini-flash-latest")
        {
            // Validate and store the Google API key
            _apiKey = string.IsNullOrWhiteSpace(apiKey)
                ? throw new ArgumentNullException(nameof(apiKey), "Google API Key is required.")
                : apiKey;

            // Set model with fallback to a capable default
            _model = model;

            // Create HttpClient with extended timeout for large context windows and multimodal processing
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        }

        /// <summary>
        /// Generates a response from the Gemini model using the provided conversation history and optional tools.
        /// </summary>
        /// <param name="history">The full conversation history, including system instructions, user, assistant, and tool messages.</param>
        /// <param name="tools">Optional list of tools the model is allowed to call.</param>
        /// <returns>An <see cref="IChatResponse"/> containing the generated text and any tool calls.</returns>
        public async Task<IChatResponse> GenerateResponseAsync(List<IChatMessage> history, List<IAgentTool>? tools = null)
        {
            // Initialize request object matching Google's Gemini API schema
            var request = new GeminiRequest();

            // 1. Extract and set System Instruction (Gemini uses a dedicated field, not a regular message)
            var systemMsg = history.OfType<ChatMessage>()
                .FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));

            if (systemMsg != null)
            {
                request.SystemInstruction = new Content
                {
                    Parts = new List<Part> { new Part { Text = systemMsg.Content.ToString() } }
                };
            }

            // 2. Map conversation history (excluding system message) to Gemini's Content format
            var chatHistory = history.OfType<ChatMessage>()
                .Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));

            foreach (var msg in chatHistory)
            {
                // Convert framework role to Gemini-specific role
                var content = new Content { Role = MapRole(msg.Role) };

                // Handle Tool Execution Results
                if (msg.Role.Equals("tool", StringComparison.OrdinalIgnoreCase))
                {
                    content.Parts.Add(new Part
                    {
                        FunctionResponse = new FunctionResponse
                        {
                            Name = msg.ToolName ?? "unknown_tool",
                            // Gemini expects tool results wrapped in a JSON object (commonly under "result")
                            Response = new { result = msg.Content }
                        }
                    });
                }
                // Handle Multimodal Content (Images / Media)
                else if (msg.MediaData != null)
                {
                    // Add accompanying text if present
                    if (!string.IsNullOrWhiteSpace(msg.Content?.ToString()))
                    {
                        content.Parts.Add(new Part { Text = msg.Content.ToString() });
                    }

                    // Add inline base64 image data
                    content.Parts.Add(new Part
                    {
                        InlineData = new InlineData
                        {
                            MimeType = msg.MimeType ?? "image/jpeg",
                            Base64Data = msg.MediaData
                        }
                    });
                }
                // Handle standard text messages
                else
                {
                    content.Parts.Add(new Part { Text = msg.Content?.ToString() ?? string.Empty });
                }

                request.Contents.Add(content);
            }

            // 3. Register available tools (function declarations)
            if (tools != null && tools.Any())
            {
                request.Tools = new List<Tool>
                {
                    new Tool
                    {
                        FunctionDeclarations = tools.Select(t => t.GetToolDefinition()).ToList()
                    }
                };
            }

            // 4. Execute API call
            // Build dynamic endpoint URL with model and API key
            string endpointUrl = $"{ApiBaseUri}/{_model}:generateContent?key={_apiKey}";

            string jsonBody = JsonSerializer.Serialize(request, _jsonOptions);
            var httpContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(endpointUrl, httpContent);
            string responseJson = await response.Content.ReadAsStringAsync();

            // Handle non-success HTTP responses
            if (!response.IsSuccessStatusCode)
            {
                return new LlmResponse { Content = $"Gemini API Error: {response.StatusCode} | {responseJson}" };
            }

            // 5. Parse successful response
            var geminiResult = JsonSerializer.Deserialize<GeminiResponse>(responseJson, _jsonOptions);
            var candidateContent = geminiResult?.Candidates?.FirstOrDefault()?.Content;

            if (candidateContent == null)
            {
                return new LlmResponse { Content = "Error: No candidate response returned from Gemini." };
            }

            var result = new LlmResponse();
            var toolCalls = new List<ToolCall>();

            // Extract text content and function calls from response parts
            foreach (var part in candidateContent.Parts)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    result.Content += part.Text;
                }

                if (part.FunctionCall != null)
                {
                    toolCalls.Add(new ToolCall
                    {
                        ToolName = part.FunctionCall.Name,
                        // Serialize arguments back to JSON string for the framework
                        ArgumentsJson = JsonSerializer.Serialize(part.FunctionCall.Args)
                    });
                }
            }

            result.ToolCalls = toolCalls.Any() ? toolCalls : null;

            return result;
        }

        /// <summary>
        /// Maps internal framework role names to Google's Gemini-specific role vocabulary.
        /// </summary>
        /// <param name="frameworkRole">The role used in the OzzieAI framework.</param>
        /// <returns>The corresponding Gemini role name.</returns>
        private string MapRole(string frameworkRole) => frameworkRole.ToLower() switch
        {
            "assistant" => "model",   // Gemini refers to the AI as "model"
            "tool" => "function", // Tool responses come from "function" role
            _ => "user"     // Everything else is treated as user input
        };

        /// <summary>
        /// Performs a lightweight health check against the Gemini API to verify API key validity and model accessibility.
        /// </summary>
        /// <returns><c>true</c> if the API responds successfully; otherwise <c>false</c>.</returns>
        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                // Query model metadata to check connectivity and permissions
                var response = await _http.GetAsync($"{ApiBaseUri}/{_model}?key={_apiKey}");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                // Any exception (network, auth, timeout, etc.) indicates unhealthy state
                return false;
            }
        }

        #region Internal Google DTOs (Data Transfer Objects)

        /// <summary>
        /// Root request object for Gemini's generateContent endpoint.
        /// </summary>
        private class GeminiRequest
        {
            public Content? SystemInstruction { get; set; }
            public List<Content> Contents { get; set; } = new();
            public List<Tool>? Tools { get; set; }
        }

        /// <summary>
        /// Represents a single turn in the conversation (user or model).
        /// </summary>
        private class Content
        {
            public string Role { get; set; } = "user";
            public List<Part> Parts { get; set; } = new();
        }

        /// <summary>
        /// A single part of content, which can be text, inline media, or function-related data.
        /// </summary>
        private class Part
        {
            public string? Text { get; set; }
            public InlineData? InlineData { get; set; }
            public FunctionCall? FunctionCall { get; set; }
            public FunctionResponse? FunctionResponse { get; set; }
        }

        /// <summary>
        /// Inline base64-encoded media data (primarily for images).
        /// </summary>
        private class InlineData
        {
            public string MimeType { get; set; } = string.Empty;
            public string Base64Data { get; set; } = string.Empty;
        }

        /// <summary>
        /// Represents a function call requested by the Gemini model.
        /// </summary>
        private class FunctionCall
        {
            public string Name { get; set; } = string.Empty;
            public object? Args { get; set; }
        }

        /// <summary>
        /// Represents the response returned from a previously executed function/tool.
        /// </summary>
        private class FunctionResponse
        {
            public string Name { get; set; } = string.Empty;
            public object? Response { get; set; }
        }

        /// <summary>
        /// Container for function declarations (tools) sent to Gemini.
        /// </summary>
        private class Tool
        {
            public List<object> FunctionDeclarations { get; set; } = new();
        }

        /// <summary>
        /// Root response object from Gemini's generateContent endpoint.
        /// </summary>
        private class GeminiResponse
        {
            public List<Candidate>? Candidates { get; set; }
        }

        /// <summary>
        /// A single candidate response from the model.
        /// </summary>
        private class Candidate
        {
            public Content? Content { get; set; }
        }

        #endregion
    }
}