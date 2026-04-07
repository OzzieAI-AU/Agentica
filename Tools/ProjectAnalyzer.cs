namespace OzzieAI.Agentica.Tools
{

    using System;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Provides the LLM with a structural overview of the current project workspace.
    /// Essential for "Boss" and "Manager" roles to understand scope.
    /// </summary>
    public class ProjectAnalyzerTool : IAgentTool
    {
        public string Name => "project_analyzer";
        public string Description => "Generates a directory tree map of the current project to understand file structure.";

        public object GetToolDefinition() => new
        {
            type = "function",
            function = new
            {
                name = Name,
                description = Description,
                parameters = new { type = "object", properties = new { } } // No params needed
            }
        };

        public Task<string> ExecuteAsync(string jsonArguments)
        {
            try
            {
                string root = Directory.GetCurrentDirectory();
                var sb = new StringBuilder();
                sb.AppendLine($"Project Root: {root}");
                BuildTree(new DirectoryInfo(root), sb, "");
                return Task.FromResult(sb.ToString());
            }
            catch (Exception ex) { return Task.FromResult($"Error mapping project: {ex.Message}"); }
        }

        private void BuildTree(DirectoryInfo dir, StringBuilder sb, string indent)
        {
            // Filter out noise like .git or bin/obj folders
            if (dir.Name == "bin" || dir.Name == "obj" || dir.Name == ".git") return;

            sb.AppendLine($"{indent}└── {dir.Name}/");
            foreach (var file in dir.GetFiles("*.cs")) // Focus on source code
                sb.AppendLine($"{indent}    ├── {file.Name}");

            foreach (var subDir in dir.GetDirectories())
                BuildTree(subDir, sb, indent + "    ");
        }
    }
}