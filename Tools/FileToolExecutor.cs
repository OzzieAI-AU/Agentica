namespace OzzieAI.Agentica.Tools
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;

    /// <summary>
    /// Provides the Agent with the ability to Read, Write, and List files on the physical disk.
    /// This tool acts as the 'Hands' for the Worker Agent.
    /// </summary>
    public class FileToolExecutor : IAgentTool
    {
        public string Name => "file_manager";
        public string Description => "Read, write, or list files in the project directory. Actions: 'read', 'write', 'list'.";

        /// <summary>
        /// Defines the JSON Schema that the LLM uses to understand how to call this tool.
        /// </summary>
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
                        action = new { type = "string", @enum = new[] { "read", "write", "list" }, description = "The operation to perform." },
                        path = new { type = "string", description = "The relative or absolute path to the file/directory." },
                        content = new { type = "string", description = "The content to write (only required for 'write' action)." }
                    },
                    required = new[] { "action", "path" }
                }
            }
        };

        /// <summary>
        /// The main entry point for the Agent. Parses the JSON arguments and executes the IO task.
        /// </summary>
        public async Task<string> ExecuteAsync(string jsonArguments)
        {
            try
            {
                // Deserialize the arguments sent by the LLM (Gemini/Grok/Ollama)
                var args = JsonSerializer.Deserialize<FileArgs>(jsonArguments, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (args == null) return "Error: Could not parse arguments.";

                // Execute the requested action asynchronously
                return args.Action.ToLower() switch
                {
                    "read" => await File.ReadAllTextAsync(args.Path),
                    "list" => string.Join(Environment.NewLine, Directory.GetFileSystemEntries(args.Path).Select(Path.GetFileName)),
                    "write" => await WriteSafelyAsync(args.Path, args.Content),
                    _ => $"Error: Unknown action '{args.Action}'."
                };
            }
            catch (Exception ex)
            {
                return $"IO Error: {ex.Message}";
            }
        }

        private async Task<string> WriteSafelyAsync(string path, string? content)
        {
            if (content == null) return "Error: No content provided for write action.";

            // Ensure the directory exists before writing
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllTextAsync(path, content);
            return $"Successfully wrote {content.Length} characters to {path}";
        }

        /// <summary>
        /// Internal DTO for parsing LLM arguments.
        /// </summary>
        private class FileArgs
        {
            public string Action { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public string? Content { get; set; }
        }
    }
}