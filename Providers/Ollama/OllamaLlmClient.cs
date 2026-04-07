namespace OzzieAI.Agentica.Providers.Ollama
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
    /// High-performance local provider for Ollama.
    /// Supports Multimodal (Images) and Tool Calling.
    /// </summary>
    public class OllamaLlmClient : ILlmProvider
    {

        private readonly HttpClient _http;
        private readonly string _model;
        private readonly string _baseUrl;


        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
        
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };


        public OllamaLlmClient(string model = "llama3.1", string baseUrl = "http://192.168.3.3:11434")
        {
        
            _model = model;
            _baseUrl = baseUrl.TrimEnd('/');
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        }


        /// <summary>
        /// Performs a deep diagnostic check of the Ollama service.
        /// Verifies network connectivity, API responsiveness, and model availability.
        /// </summary>
        /// <returns>
        /// True if the Ollama server is reachable and the configured model is pulled/ready; 
        /// otherwise, false.
        /// </returns>
        public async Task<bool> CheckHeartbeatAsync()
        {
            try
            {
                // 1. Basic Connectivity Check (Is the service running?)
                // We use /api/tags as it is a lightweight GET request that returns all local models.
                var response = await _http.GetAsync($"{_baseUrl}/api/tags");

                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                // 2. Model Availability Check (Is our specific model pulled?)
                // Even if the server is up, the agent fails if the 'llama3.1' (or similar) isn't there.
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("models", out var modelsElement))
                {
                    foreach (var model in modelsElement.EnumerateArray())
                    {
                        if (model.TryGetProperty("name", out var nameProp))
                        {
                            string? actualName = nameProp.GetString();

                            // Check for exact match or tag-less match (e.g., 'llama3.1:latest' vs 'llama3.1')
                            if (actualName != null && (actualName.Contains(_model) || _model.Contains(actualName)))
                            {
                                return true;
                            }
                        }
                    }
                }

                // If we reach here, the server is up but the specific model is missing.
                return false;
            }
            catch (HttpRequestException)
            {
                // Handles cases where the IP/Port is unreachable (Server is down)
                return false;
            }
            catch (TaskCanceledException)
            {
                // Handles request timeouts (Server is hanging)
                return false;
            }
            catch (Exception)
            {
                // General safety for unexpected parsing errors
                return false;
            }
        }

        /// <summary>
        /// Implementation of the ILlmProvider health check.
        /// Wraps the deep heartbeat check for unified framework monitoring.
        /// </summary>
        public async Task<bool> IsHealthyAsync() => await CheckHeartbeatAsync();

        
        /// <summary>
        /// Generates a response from the local Ollama instance.
        /// Maps Agentica messages to Ollama's specific message schema.
        /// </summary>
        public async Task<IChatResponse> GenerateResponseAsync(List<IChatMessage> history, List<IAgentTool>? tools = null)
        {
            var request = new OllamaChatRequest
            {
                Model = _model,
                Stream = false,
                Tools = tools?.Select(t => t.GetToolDefinition()).ToList(),
                Messages = history.Select(m => new OllamaMessage
                {
                    Role = ((ChatMessage)m).Role.ToLower() == "tool" ? "tool" : ((ChatMessage)m).Role, // Ollama uses 'tool' role for results
                    Content = ((ChatMessage)m).Content.ToString(),
                    Images = ((ChatMessage)m).MediaData != null ? new List<string> { ((ChatMessage)m).MediaData } : null
                }).ToList()
            };

            try
            {
                string json = JsonSerializer.Serialize(request, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _http.PostAsync($"{_baseUrl}/api/chat", content);
                response.EnsureSuccessStatusCode();

                string resultJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(resultJson);
                var messageElement = doc.RootElement.GetProperty("message");

                var result = new LlmResponse();

                // 1. Extract Text Content
                if (messageElement.TryGetProperty("content", out var contentProp))
                {
                    result.Content = contentProp.GetString() ?? "";
                }

                // 2. Extract Tool Calls
                if (messageElement.TryGetProperty("tool_calls", out var toolCallsProp))
                {
                    var calls = new List<ToolCall>();
                    foreach (var call in toolCallsProp.EnumerateArray())
                    {
                        var func = call.GetProperty("function");
                        calls.Add(new ToolCall
                        {
                            ToolName = func.GetProperty("name").GetString() ?? "",
                            ArgumentsJson = func.GetProperty("arguments").GetRawText()
                        });
                    }
                    result.ToolCalls = calls.Any() ? calls : null;
                }

                return result;
            }
            catch (Exception ex)
            {
                return new LlmResponse { Content = $"Ollama Local Error: {ex.Message}" };
            }
        }
    }
}