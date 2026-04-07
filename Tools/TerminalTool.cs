namespace OzzieAI.Agentica.Tools
{

    using System;
    using System.Diagnostics;
    using System.Text.Json;
    using System.Threading.Tasks;

    /// <summary>
    /// Enables the Agent to execute CLI commands within the project environment.
    /// Use with caution: This provides the Agent with shell access.
    /// </summary>
    public class TerminalTool : IAgentTool
    {
        public string Name => "terminal_executor";
        public string Description => "Executes shell commands (e.g., dotnet build, dotnet run) and returns the output.";

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
                        command = new { type = "string", description = "The full command string to execute." }
                    },
                    required = new[] { "command" }
                }
            }
        };

        public async Task<string> ExecuteAsync(string jsonArguments)
        {
            var options = JsonSerializer.Deserialize<TerminalArgs>(jsonArguments);
            if (string.IsNullOrEmpty(options?.Command)) return "Error: No command provided.";

            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe", // Or "/bin/bash" for Linux/Mac
                    Arguments = $"/c {options.Command}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                return string.IsNullOrEmpty(error) ? output : $"STDOUT: {output}\nSTDERR: {error}";
            }
            catch (Exception ex) { return $"Execution Failed: {ex.Message}"; }
        }

        private class TerminalArgs { public string Command { get; set; } = ""; }
    }
}