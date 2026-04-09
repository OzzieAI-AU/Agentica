namespace OzzieAI.Agentica
{
    using OzzieAI.Agentica.Providers;
    using OzzieAI.Agentica.Tools;
    using System;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Concrete implementation of a Worker Agent that inherits from <see cref="BaseAgent"/>.
    /// Responsible for receiving task assignments, executing the full reasoning + tool-calling loop,
    /// and returning a final result back to the sender (typically a Manager or Boss agent).
    /// Includes rich, color-coded console logging for clear visibility of progress and status.
    /// </summary>
    public class WorkerAgent : BaseAgent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WorkerAgent"/> class.
        /// </summary>
        /// <param name="config">The configuration settings for this worker agent.</param>
        /// <param name="bus">The agent message bus for inter-agent communication.</param>
        /// <param name="memory">The agent's memory instance for conversation history.</param>
        public WorkerAgent(AgentConfig config, IAgentBus bus, DragonMemory memory)
            : base(config, bus, memory)
        {
            // No additional initialization required — all setup is handled in BaseAgent
        }

        /// <summary>
        /// Processes incoming messages from the agent bus.
        /// Currently handles <see cref="MessageType.TaskAssignment"/> messages by starting
        /// the reasoning loop and returning the final result.
        /// </summary>
        /// <param name="message">The incoming message to process.</param>
        protected override async Task ProcessIncomingMessageAsync(AgentMessage message)
        {
            try
            {
                // Only process TaskAssignment messages for this worker agent
                if (message.Type == MessageType.TaskAssignment)
                {
                    // Log receipt of new task with visual emphasis:
                    ConsoleLogger.WriteLine($"\n[Worker {Config.Name}] 🚀 RECEIVED TASK:", ConsoleColor.Yellow);
                    ConsoleLogger.WriteLine($"   {message.Content}", ConsoleColor.Yellow);

                    // Indicate start of reasoning and tool execution:
                    ConsoleLogger.WriteLine($"[Worker {Config.Name}] 🧠 Starting reasoning + tool execution loop...", ConsoleColor.Blue);

                    // Add the task directly to memory history (in addition to StartTaskAsync)
                    Memory.History.Add(new ChatMessage { Role = "user", Content = message.Content });

                    // Begin the agent's reasoning loop (inherited from BaseAgent)
                    await StartTaskAsync(message.Content);

                    ConsoleLogger.WriteLine($"[Worker {Config.Name}] 🔄 Reasoning loop finished.", ConsoleColor.Yellow);

                    // Extract the final assistant message as the completed report
                    var lastAssistant = Memory.History
                        .LastOrDefault(m => m.Role?.Equals("assistant", StringComparison.OrdinalIgnoreCase) == true);

                    string finalReport = lastAssistant?.Content?.ToString()
                        ?? "[No final report from LLM]";

                    // Log successful task completion:
                    ConsoleLogger.WriteLine($"[Worker {Config.Name}] ✅ TASK COMPLETED", ConsoleColor.Green);
                    ConsoleLogger.WriteLine($"   Final Report: {finalReport}", ConsoleColor.Green);

                    // Send the final result back to the original sender (Manager/Boss)
                    await Bus.SendAsync(new AgentMessage(
                        Config.Id,                    // Sender = this worker
                        message.SenderId,             // Recipient = original requester
                        MessageType.TaskResult,       // Message type
                        finalReport,                  // Payload = final report
                        DateTime.UtcNow));

                    // Confirm the result was sent:
                    ConsoleLogger.WriteLine($"[Worker {Config.Name}] 📤 Sent result back to Manager/Boss", ConsoleColor.DarkYellow);
                }
            }
            catch (Exception ex)
            {
                // Log any unexpected errors during message processing:
                ConsoleLogger.WriteLine($"[Worker {Config.Name}] 💥 ERROR: {ex.Message}", ConsoleColor.Red);
            }
        }
    }
}