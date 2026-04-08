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
    /// Handles multimodal inputs, system instructions, and functional tool-calling logic.
    /// </summary>
    public class GeminiProvider : ILlmProvider
    {
        // Immutable dependencies and configurations
        private readonly string _apiKey;
        private readonly string _model;
        private readonly HttpClient _http;

        // Base URI for the Google Generative AI REST API
        private const string ApiBaseUri = "https://generativelanguage.googleapis.com/v1beta/models";

        // Configured JSON options to strictly adhere to Google's camelCase API requirements
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true // Ensures we can read responses gracefully
        };

        /// <summary>
        /// Initializes a new instance of the GeminiProvider.
        /// </summary>
        /// <param name="apiKey">Your Google AI Studio API key.</param>
        /// <param name="model">The specific model identifier (e.g., 'gemini-1.5-pro' or 'gemini-1.5-flash').</param>
        /// <exception cref="ArgumentNullException">Thrown if the API key is missing.</exception>
        public GeminiProvider(string apiKey, string model)
        {
            // Validate and assign the API key
            _apiKey = string.IsNullOrWhiteSpace(apiKey)
                ? throw new ArgumentNullException(nameof(apiKey), "Google API Key is required.")
                : apiKey;

            // Parse and assign the model. Default to 1.5-pro if none is provided.
            _model = string.IsNullOrWhiteSpace(model) ? "gemini-1.5-pro" : model;

            // Initialize the HTTP Client with an extended timeout. 
            // Gemini processes massive context windows (up to 2M tokens) and multimodal data, 
            // which can take significantly longer than standard text generation.
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        }

        /// <summary>
        /// Generates an AI response based on the provided conversation history and available tools.
        /// </summary>
        /// <param name="history">The chronological chat history, including system prompts and user/assistant messages.</param>
        /// <param name="tools">An optional list of tools the agent is permitted to invoke.</param>
        /// <returns>A structured response containing the AI's text and/or requested tool calls.</returns>
        public async Task<IChatResponse> GenerateResponseAsync(List<IChatMessage> history, List<IAgentTool>? tools = null)
        {
            // Initialize the primary request object matching Google's schema
            var request = new GeminiRequest();

            // 1. System Instruction Extraction
            // Gemini requires system instructions to be passed in a dedicated field, not as a standard chat message.
            var systemMsg = history.OfType<ChatMessage>()
                .FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));

            if (systemMsg != null)
            {
                request.SystemInstruction = new Content
                {
                    Parts = new List<Part> { new Part { Text = systemMsg.Content.ToString() } }
                };
            }

            // 2. Multimodal History Mapping
            // Iterate over all non-system messages and map them to Gemini's 'Content' schema.
            var chatHistory = history.OfType<ChatMessage>()
                .Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));

            foreach (var msg in chatHistory)
            {
                // Map the framework's internal role (e.g., 'assistant') to Gemini's expected role (e.g., 'model')
                var content = new Content { Role = MapRole(msg.Role) };

                // Handle Tool Responses (Data returning from a tool execution back to the LLM)
                if (msg.Role.Equals("tool", StringComparison.OrdinalIgnoreCase))
                {
                    content.Parts.Add(new Part
                    {
                        FunctionResponse = new FunctionResponse
                        {
                            Name = msg.ToolName ?? "unknown_tool",
                            // Gemini expects the response inside a JSON object, typically keyed by "result"
                            Response = new { result = msg.Content }
                        }
                    });
                }
                // Handle Multimodal (Image/Media) Context
                else if (msg.MediaData != null)
                {
                    // Include any accompanying text alongside the image
                    if (!string.IsNullOrWhiteSpace(msg.Content?.ToString()))
                    {
                        content.Parts.Add(new Part { Text = msg.Content.ToString() });
                    }

                    // Attach the Base64 encoded media data
                    content.Parts.Add(new Part
                    {
                        InlineData = new InlineData
                        {
                            MimeType = msg.MimeType ?? "image/jpeg",
                            Base64Data = msg.MediaData
                        }
                    });
                }
                // Handle Standard Text Context
                else
                {
                    content.Parts.Add(new Part { Text = msg.Content?.ToString() ?? string.Empty });
                }

                request.Contents.Add(content);
            }

            // 3. Tool Registration
            // If tools are provided, convert their definitions into Gemini's 'FunctionDeclarations' format
            if (tools != null && tools.Any())
            {
                request.Tools = new List<Tool>
                {
                    new Tool { FunctionDeclarations = tools.Select(t => t.GetToolDefinition()).ToList() }
                };
            }

            // 4. API Execution
            // Construct the dynamic URL using the parsed model from the constructor
            string endpointUrl = $"{ApiBaseUri}/{_model}:generateContent?key={_apiKey}";

            // Serialize the payload and create the HTTP content
            string jsonBody = JsonSerializer.Serialize(request, _jsonOptions);
            var httpContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            // Execute the POST request to the Google API
            var response = await _http.PostAsync(endpointUrl, httpContent);
            string responseJson = await response.Content.ReadAsStringAsync();

            // Handle HTTP-level failures
            if (!response.IsSuccessStatusCode)
            {
                return new LlmResponse { Content = $"Gemini API Error: {response.StatusCode} | {responseJson}" };
            }

            // 5. Response Parsing
            // Deserialize the successful JSON payload back into our internal C# models
            var geminiResult = JsonSerializer.Deserialize<GeminiResponse>(responseJson, _jsonOptions);
            var candidateContent = geminiResult?.Candidates?.FirstOrDefault()?.Content;

            // Safety check for empty or blocked responses
            if (candidateContent == null)
            {
                return new LlmResponse { Content = "Error: No candidate response returned from Gemini." };
            }

            var result = new LlmResponse();
            var toolCalls = new List<ToolCall>();

            // Iterate through the parts of the response to extract text and function calls
            foreach (var part in candidateContent.Parts)
            {
                // Accumulate standard text responses
                if (!string.IsNullOrEmpty(part.Text))
                {
                    result.Content += part.Text;
                }

                // Accumulate requested tool calls
                if (part.FunctionCall != null)
                {
                    toolCalls.Add(new ToolCall
                    {
                        ToolName = part.FunctionCall.Name,
                        // Serialize the arguments object back to a JSON string for the framework's executor
                        ArgumentsJson = JsonSerializer.Serialize(part.FunctionCall.Args)
                    });
                }
            }

            // Assign parsed tool calls to the result, or null if none exist
            result.ToolCalls = toolCalls.Any() ? toolCalls : null;
            return result;
        }

        /// <summary>
        /// Translates framework-agnostic roles into Google Gemini's specific role vocabulary.
        /// </summary>
        private string MapRole(string frameworkRole) => frameworkRole.ToLower() switch
        {
            "assistant" => "model",    // Gemini calls the AI the 'model'
            "tool" => "function",      // Gemini expects tool results to come from a 'function' role
            _ => "user"                // Default all other input to 'user'
        };

        /// <summary>
        /// Performs a lightweight ping to the Google API to verify the API key and model availability.
        /// </summary>
        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                // Dynamically constructs the GET request for the specific model info
                var response = await _http.GetAsync($"{ApiBaseUri}/{_model}?key={_apiKey}");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        #region Internal Google DTOs (Data Transfer Objects)
        // These private classes define the strict JSON schema expected by the Gemini API.

        private class GeminiRequest
        {
            public Content? SystemInstruction { get; set; }
            public List<Content> Contents { get; set; } = new();
            public List<Tool>? Tools { get; set; }
        }

        private class Content
        {
            public string Role { get; set; } = "user";
            public List<Part> Parts { get; set; } = new();
        }

        private class Part
        {
            public string? Text { get; set; }
            public InlineData? InlineData { get; set; }
            public FunctionCall? FunctionCall { get; set; }
            public FunctionResponse? FunctionResponse { get; set; }
        }

        private class InlineData
        {
            public string MimeType { get; set; } = string.Empty;
            public string Base64Data { get; set; } = string.Empty;
        }

        private class FunctionCall
        {
            public string Name { get; set; } = string.Empty;
            public object? Args { get; set; }
        }

        private class FunctionResponse
        {
            public string Name { get; set; } = string.Empty;
            public object? Response { get; set; }
        }

        private class Tool
        {
            public List<object> FunctionDeclarations { get; set; } = new();
        }

        private class GeminiResponse
        {
            public List<Candidate>? Candidates { get; set; }
        }

        private class Candidate
        {
            public Content? Content { get; set; }
        }
        #endregion
    }
}