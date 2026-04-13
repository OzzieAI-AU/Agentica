namespace OzzieAI.Agentica
{
    using OzzieAI.Agentica.Agents;
    using System;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public class WorkerAgent : BaseAgent
    {
        public WorkerAgent(AgentConfig config, IAgentBus bus, DragonMemory memory)
            : base(config, bus, memory) { }

        protected override async Task ProcessIncomingMessageAsync(AgentMessage message)
        {
            if (message.Type != MessageType.TaskAssignment) return;

            ConsoleLogger.WriteLine($"[{Config.Name}] 📥 Processing Task...", ConsoleColor.Green);

            try
            {
                // Run the Think-Act-Observe cycle
                await StartTaskAsync(message.Content);

                // AGGREGATION: Collect ALL assistant responses from this session.
                // This ensures if the LLM wrote code in step 2 but a summary in step 5, the Manager gets BOTH.
                var sessionResults = Memory.History
                    .Where(m => m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                    .Select(m => m.Content.ToString());

                StringBuilder fullOutput = new StringBuilder();
                fullOutput.AppendLine("--- WORKER EXECUTION SUMMARY ---");
                foreach (var part in sessionResults)
                {
                    fullOutput.AppendLine(part);
                    fullOutput.AppendLine("---");
                }

                string finalResult = fullOutput.ToString();

                // Persist & Dispatch (SINGLE SEND)
                Memory.Remember($"Result_{Guid.NewGuid().ToString()[..4]}", finalResult, propagate: true);

                await Bus.SendAsync(new AgentMessage(
                    SenderId: Config.Id,
                    ReceiverId: Config.ManagerId ?? message.SenderId,
                    Type: MessageType.TaskResult,
                    Content: finalResult,
                    Timestamp: DateTime.UtcNow));

                ConsoleLogger.WriteLine($"[{Config.Name}] ✅ Result dispatched to Manager.", ConsoleColor.DarkYellow);
            }
            catch (Exception ex)
            {
                ConsoleLogger.WriteLine($"[{Config.Name}] 💥 Error: {ex.Message}", ConsoleColor.Red);
            }
        }
    }
}