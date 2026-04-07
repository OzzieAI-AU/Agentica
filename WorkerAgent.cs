namespace OzzieAI.Agentica
{
    using OzzieAI.Agentica.Providers;
    using OzzieAI.Agentica.Tools;
    using System;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Represents a high-autonomy execution entity designed for technical implementation.
    /// The WorkerAgent is optimized for 'Action-Oriented' tasks, utilizing a suite of 
    /// physical tools (File IO, Terminal, Search) to achieve concrete project milestones.
    /// </summary>
    public class WorkerAgent : BaseAgent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WorkerAgent"/>.
        /// </summary>
        public WorkerAgent(AgentConfig config, IAgentBus bus, DragonMemory memory)
            : base(config, bus, memory) { }

        /// <summary>
        /// Handles operational assignments from the AgentBus.
        /// When a <see cref="MessageType.TaskAssignment"/> is received, the Worker triggers 
        /// its internal ReAct (Reason + Act) loop to fulfill the request.
        /// </summary>
        /// <param name="message">The task metadata and content sent by a Manager or Boss.</param>
        protected override async Task ProcessIncomingMessageAsync(AgentMessage message)
        {
            if (message.Type == MessageType.TaskAssignment)
            {
                // Visual signaling that the 'Hands' of the organization are moving
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[Worker {Config.Name}] Task Received: {message.Content}");
                Console.ResetColor();

                // 1. Ignition: Enter the recursive Thinking Loop inherited from BaseAgent.
                // This kicks off the loop where the LLM can autonomously decide to call:
                // 'file_manager' to read/write, 'terminal_executor' to build, or 'web_search' to debug.
                await StartTaskAsync(message.Content);

                // 2. Extraction: Identify the final conclusion from the cognitive history.
                // We look for the most recent Assistant message which should contain 
                // the summary of the work performed or the error encountered.
                var lastAssistantMessage = Memory.History
                    .LastOrDefault(m => m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase));

                string finalReport = lastAssistantMessage?.Content as string ?? "Task processed, but no descriptive report was generated.";

                // 3. Reporting: Close the loop by sending the result back to the Requester.
                // This informs the Manager that the task is complete and provides the evidence.
                await Bus.SendAsync(new AgentMessage(
                    SenderId: Config.Id,
                    ReceiverId: message.SenderId,
                    Type: MessageType.TaskResult,
                    Content: finalReport,
                    Timestamp: DateTime.UtcNow));

                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"[Worker {Config.Name}] Task Complete. Reporting back to {message.SenderId}.");
                Console.ResetColor();
            }
        }
    }
}