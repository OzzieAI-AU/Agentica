namespace OzzieAI.Agentica.Tools
{
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;

    /// <summary>
    /// The Terminal Tool acts as the "Hands and Keyboard" of the AI Agent.
    /// It allows the AI to type commands into the computer (like building code, moving files, or running tests)
    /// and read the results that come back on the screen.
    /// </summary>
    public class TerminalTool : IAgentTool
    {
        /// <summary>
        /// The official name badge for this tool. The AI uses this name to call it.
        /// </summary>
        public string Name => "terminal_executor";

        /// <summary>
        /// The instruction manual for the AI. It tells the AI exactly when and why to use this tool.
        /// </summary>
        public string Description => "Executes system CLI/Shell commands (e.g., 'dotnet build', 'dir', 'ls') and reads the output terminal text.";

        /// <summary>
        /// The "Blueprint" that tells the AI how to pack its request.
        /// It strictly requires a JSON package with one item: a string called "command".
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
                        command = new { type = "string", description = "The full command string to type into the terminal." }
                    },
                    required = new[] { "command" }
                }
            }
        };

        /// <summary>
        /// The main engine of the tool. It takes the AI's JSON request, unpacks it, 
        /// types it into the computer's actual terminal, waits for the result, and hands it back.
        /// </summary>
        public async Task<string> ExecuteAsync(string jsonArguments)
        {
            try
            {
                // 1. Unpack the AI's request safely (Ignoring upper/lower case mistakes)
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var args = JsonSerializer.Deserialize<TerminalArgs>(jsonArguments, options);

                if (string.IsNullOrWhiteSpace(args?.Command))
                    return "Error: No command provided in the JSON payload.";

                // 2. Detect the computer type (Windows vs Mac/Linux)
                // Windows uses 'cmd.exe', while Mac and Linux use '/bin/bash'.
                bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                string shellProgram = isWindows ? "cmd.exe" : "/bin/bash";
                string shellArguments = isWindows ? $"/c {args.Command}" : $"-c \"{args.Command}\"";

                // 3. Prepare the invisible Terminal Window
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = shellProgram,
                    Arguments = shellArguments,
                    RedirectStandardOutput = true, // Capture standard text
                    RedirectStandardError = true,  // Capture red error text
                    UseShellExecute = false,       // Run in the background
                    CreateNoWindow = true,         // Do not flash a black box on the screen
                    WorkingDirectory = Environment.CurrentDirectory // Run where the project lives
                };

                // 4. Press "Enter" and start the command
                process.Start();

                // 5. Start a timer and start reading the screen
                // We give the command a maximum of 30 seconds to finish. If it's a server that runs forever, we stop it.
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                var exitTask = process.WaitForExitAsync();

                // Wait until EITHER the command finishes OR the 30-second timer rings
                var completedTask = await Task.WhenAny(exitTask, timeoutTask);

                // 6. Handle infinite loops / timeouts
                if (completedTask == timeoutTask)
                {
                    process.Kill(); // Force stop the terminal
                    return $"[TIMEOUT ERROR] The command '{args.Command}' took longer than 30 seconds to run and was forcefully stopped. If you started a server, run it in the background.";
                }

                // 7. Collect the final text from the screen
                string output = await outputTask;
                string error = await errorTask;

                // 8. Package the results cleanly for the AI to read
                if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(error))
                {
                    return $"[TERMINAL ERROR - Exit Code {process.ExitCode}]\n{error.Trim()}\n{output.Trim()}";
                }

                // If it worked perfectly but printed nothing (like creating a file)
                if (string.IsNullOrWhiteSpace(output))
                {
                    return "[COMMAND EXECUTED SUCCESSFULLY - NO TEXT OUTPUT]";
                }

                return output.Trim();
            }
            catch (Exception ex)
            {
                return $"[CRITICAL TERMINAL FAILURE]: {ex.Message}";
            }
        }

        /// <summary>
        /// A simple container to hold the AI's unpacked request.
        /// The [JsonPropertyName] tag forces C# to map the lowercase JSON "command" to our uppercase C# "Command".
        /// </summary>
        private class TerminalArgs
        {
            [JsonPropertyName("command")]
            public string Command { get; set; } = string.Empty;
        }
    }
}