namespace OzzieAI.Agentica
{
    using OzzieAI.Agentica.Agents;
    // ─────────────────────────────────────────────────────────────────────────────
    // NAMESPACE DOCUMENTATION
    // ─────────────────────────────────────────────────────────────────────────────
    // This namespace contains the complete Agentica framework — a sophisticated,
    // production-grade, swarm-native AI agent architecture developed for OzzieAI.
    // All agents, tools, providers, memory systems, and communication primitives
    // are organized here to ensure architectural clarity, thread-safety, and
    // seamless hierarchical collaboration across the entire swarm.

    using OzzieAI.Agentica.Providers;   // Contains ILlmProvider and related LLM abstractions
    using System;                       // Core .NET types including Exception, DateTime, and StringComparison
    using System.Linq;                  // Provides LINQ extension methods (LastOrDefault, etc.)
    using System.Threading.Tasks;       // Async/await infrastructure for non-blocking operations

    /// <summary>
    /// WorkerAgent — The dedicated execution engine of the OzzieAI swarm.
    /// 
    /// This concrete implementation of BaseAgent is responsible for receiving
    /// concrete tasks from the Manager and executing the full Think → Act → Observe
    /// reasoning cycle. It never terminates its listening loop, ensuring continuous
    /// availability within the swarm. Upon successful completion, it returns a
    /// clean final report to its designated parent (usually the Manager).
    /// 
    /// WorkerAgent is designed to be lightweight, focused, and extremely reliable —
    /// the "doers" that turn high-level directives into concrete results.
    /// </summary>
    public class WorkerAgent : BaseAgent
    {
        // ─────────────────────────────────────────────────────────────────────
        // CONSTRUCTOR
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Initializes a new instance of the WorkerAgent class.
        /// Forwards all dependencies to the BaseAgent constructor for
        /// centralized dependency validation and initialization.
        /// </summary>
        /// <param name="config">Agent configuration containing identity, provider, and swarm settings.</param>
        /// <param name="bus">Shared message bus for inter-agent communication.</param>
        /// <param name="memory">DragonMemory instance dedicated to this worker's history.</param>
        public WorkerAgent(AgentConfig config, IAgentBus bus, DragonMemory memory)
            : base(config, bus, memory) { }

        // ─────────────────────────────────────────────────────────────────────
        // MESSAGE PROCESSING OVERRIDE
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Processes incoming messages from the swarm bus. This is the concrete
        /// implementation of the abstract method defined in BaseAgent.
        /// 
        /// Only messages of type TaskAssignment are accepted and processed.
        /// All other message types are logged as unexpected and silently ignored,
        /// preserving the agent's focus on execution tasks only.
        /// </summary>
        /// <param name="message">The AgentMessage received from the message bus.</param>
        protected override async Task ProcessIncomingMessageAsync(AgentMessage message)
        {
            // 1. Type guard: Only process TaskAssignment messages
            if (message.Type != MessageType.TaskAssignment)
            {
                // Log unexpected message types as a warning (non-fatal)
                ConsoleLogger.WriteLine($"[{Config.Name} - (Worker)] ⚠️ Received unexpected message type: {message.Type}", ConsoleColor.Yellow);
                return;
            }

            // 2. Beautiful success log indicating a new task has arrived
            ConsoleLogger.WriteLine($"[{Config.Name} - (Worker)] ?? RECEIVED NEW TASK", ConsoleColor.Green);

            // 3. Log the actual task content for full observability
            ConsoleLogger.WriteLine($"Task: {message.Content}", ConsoleColor.Green);

            try
            {
                // 4. Kick off the full Think-Act-Observe reasoning cycle
                //    This is where the magic happens — the agent thinks, acts via tools,
                //    observes results, and iterates until completion or MaxSteps is reached.
                await StartTaskAsync(message.Content);

                // 5. Extract the final assistant message from memory (the LLM's concluding output)
                var lastAssistant = Memory.History
                    .LastOrDefault(m => m.Role?.Equals("assistant", StringComparison.OrdinalIgnoreCase) == true);

                // 6. Safely extract the final report content, with a clear fallback message
                string finalReport = lastAssistant?.Content?.ToString()
                    ?? "[No final report generated by the LLM]";

                // 7. Log successful task completion with green highlighting
                ConsoleLogger.WriteLine($"[{Config.Name} - (Worker)] ✅ TASK COMPLETED SUCCESSFULLY", ConsoleColor.Green);

                // 8. Display the final report for immediate visibility
                ConsoleLogger.WriteLine($" Final Report:\n{finalReport}", ConsoleColor.Green);

                // 9. Determine the recipient — prioritize the configured ParentId, 
                //    but fallback to the original sender to prevent [BUS WARNING] dead-letters.
                string targetRecipient = Config.ParentId;

                if (string.IsNullOrEmpty(targetRecipient))
                {
                    targetRecipient = message.SenderId;
                    ConsoleLogger.WriteLine($"[Worker {Config.Name}] ⚠️ ParentId missing, falling back to Sender: {targetRecipient}", ConsoleColor.Yellow);
                }

                // 10. Send the final result back
                await Bus.SendAsync(new AgentMessage(
                    SenderId: Config.Id,
                    ReceiverId: targetRecipient,
                    Type: MessageType.TaskResult,
                    Content: finalReport,
                    Timestamp: DateTime.UtcNow));

                // 11. Confirm the result has been dispatched
                ConsoleLogger.WriteLine($"[{Config.Name} - (Worker)] 📤 Sent final result to Manager", ConsoleColor.DarkYellow);
            }
            catch (Exception ex)
            {
                // 12. Comprehensive error handling — logs the exception but keeps the
                //     listening loop alive. One failed task will never kill the worker.
                ConsoleLogger.WriteLine($"[{Config.Name} - (Worker)] 💥 ERROR processing task: {ex.Message}", ConsoleColor.Red);
            }
        }
    }
}