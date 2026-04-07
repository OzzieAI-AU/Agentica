namespace OzzieAI.Agentica.Tools
{
    using System;
    using System.Net.Http;
    using System.Threading.Tasks;
    using System.Text.Json;

    /// <summary>
    /// Grants the Agent access to real-time web data via a Search API (e.g., Tavily or Serper).
    /// </summary>
    public class WebSearchTool : IAgentTool
    {
        public string Name => "web_search";
        public string Description => "Search the internet for real-time information, documentation, or troubleshooting steps.";

        public object GetToolDefinition() => new
        {
            type = "function",
            function = new
            {
                name = Name,
                description = Description,
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "The search query." }
                    },
                    required = new[] { "query" }
                }
            }
        };

        public async Task<string> ExecuteAsync(string jsonArguments)
        {
            try
            {
                var args = JsonSerializer.Deserialize<SearchArgs>(jsonArguments);
                if (string.IsNullOrEmpty(args?.Query)) return "Error: No query provided.";

                // Example using a generic search proxy or direct API
                using var client = new HttpClient();
                // Replace with your preferred Search API (Tavily/Google/Serper)
                return $"Search Result for '{args.Query}': [Simulated] Found latest CUDA 12.4 documentation at developer.nvidia.com";
            }
            catch (Exception ex) { return $"Search failed: {ex.Message}"; }
        }

        private class SearchArgs { public string Query { get; set; } = ""; }
    }
}