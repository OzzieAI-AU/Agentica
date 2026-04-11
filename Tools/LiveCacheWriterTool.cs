using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace OzzieAI.Agentica.Tools
{
    /// <summary>
    /// Dedicated tool for agents to request file writes. 
    /// All writes are safely routed through LiveCache (with Roslyn validation, scoring, etc.).
    /// This ensures LiveCache remains the single source of truth for file persistence.
    /// </summary>
    public class LiveCacheWriterTool : IAgentTool
    {
        public string Name => "write_file";

        public string Description => "Safely writes a file to disk through LiveCache with syntax validation and scoring. " +
                                     "Use this instead of raw file operations.";

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
                        path = new { type = "string", description = "Relative or absolute path to the file (e.g. SumCalculator.cs)" },
                        content = new { type = "string", description = "The full content to write to the file" },
                        score = new { type = "integer", description = "Initial quality score (0-100). Default 80." },
                        notes = new { type = "string", description = "Optional notes about this file." }
                    },
                    required = new[] { "path", "content" }
                }
            }
        };

        // We need access to LiveCache. We'll inject it via constructor (best practice).
        private readonly LiveCache _liveCache;

        public LiveCacheWriterTool(LiveCache liveCache)
        {
            _liveCache = liveCache ?? throw new ArgumentNullException(nameof(liveCache));
        }

        public async Task<string> ExecuteAsync(string jsonArguments)
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var args = JsonSerializer.Deserialize<WriteFileArgs>(jsonArguments, options);

                if (args == null || string.IsNullOrWhiteSpace(args.Path) || string.IsNullOrWhiteSpace(args.Content))
                    return "Error: path and content are required.";

                int score = args.Score > 0 ? args.Score : 80;
                string notes = args.Notes ?? "Written by agent via LiveCacheWriterTool";

                // === THE KEY LINE ===
                // All file writes now go through LiveCache's hardened method
                _liveCache.UpdateFileContentAndScore(args.Path, args.Content, score, notes);

                return $"✅ Successfully wrote {args.Content.Length} characters to {args.Path} (score: {score})";
            }
            catch (Exception ex)
            {
                ConsoleLogger.Error($"LiveCacheWriterTool failed: {ex.Message}");
                return $"Write failed: {ex.Message}";
            }
        }

        private class WriteFileArgs
        {
            public string Path { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
            public int Score { get; set; } = 80;
            public string? Notes { get; set; }
        }
    }
}