using System;
using System.Threading.Tasks;
using OzzieAI.Agentica.Tools;

namespace OzzieAI.Agentica.Tools
{
    /// <summary>
    /// Represents a human-in-the-loop safety mechanism for the Agentica framework.
    /// This tool acts as a circuit breaker, interrupting the autonomous agent loop 
    /// to request explicit user permission before executing potentially destructive actions.
    /// </summary>
    public class ApprovalTool : IAgentTool
    {
        /// <summary>
        /// The precise function name the LLM uses to invoke this tool.
        /// </summary>
        public string Name => "human_approval";

        /// <summary>
        /// The instruction manual for the LLM. This dictates under what conditions 
        /// the agent is required to halt and call this tool.
        /// </summary>
        public string Description => "Mandatory for destructive actions like deleting files, overwriting critical data, or running unknown terminal scripts.";

        /// <summary>
        /// Executes the human approval prompt in the console.
        /// </summary>
        /// <param name="jsonArguments">The JSON payload sent by the LLM containing the action details.</param>
        /// <returns>A string indicating whether the user approved or denied the action.</returns>
        public async Task<string> ExecuteAsync(string jsonArguments)
        {
            // Visual alert for the user:
            ConsoleLogger.WriteLine($"\n[ACTION PENDING - APPROVAL REQUIRED]", ConsoleColor.Red);
            ConsoleLogger.WriteLine($"Details: {jsonArguments}", ConsoleColor.White);

            // Prompt for user input
            Console.Write("Allow this action? (y/n): ");

            // Read and evaluate the response
            var input = Console.ReadLine();
            return input?.Trim().ToLower() == "y"
                ? "Approved"
                : "Denied by user. You must abort the current destructive action.";
        }

        /// <summary>
        /// Generates the JSON Schema definition so the LLM understands how to construct the arguments.
        /// </summary>
        /// <returns>An object representing the function signature, ready for serialization.</returns>
        public object GetToolDefinition()
        {
            return new
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
                            action_to_approve = new
                            {
                                type = "string",
                                description = "A detailed, plain-English description of the action you are about to perform. Explain exactly what files or commands are involved so the user can make an informed decision."
                            }
                        },
                        required = new[] { "action_to_approve" }
                    }
                }
            };
        }
    }
}