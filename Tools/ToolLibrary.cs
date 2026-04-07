namespace OzzieAI.Agentica.Tools
{
    public static class ToolLibrary
    {
        public static object FileManagerTool = new
        {
            type = "function",
            function = new
            {
                name = "file_manager",
                description = "Read, write, or list files in the project directory.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        action = new { type = "string", @enum = new[] { "read", "write", "list" } },
                        path = new { type = "string", description = "The relative path to the file." },
                        content = new { type = "string", description = "Content to write (only for write action)." }
                    },
                    required = new[] { "action", "path" }
                }
            }
        };
    }
}