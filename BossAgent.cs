namespace OzzieAI.Agentica
{
    using OzzieAI.Agentica.Providers;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The high-level orchestrator and strategic decision-maker of the Agentica ecosystem.
    /// The BossAgent manages company-wide skills, oversees project health via scheduled 
    /// diagnostics, and resolves 'DecisionRequests' sent by subordinate agents.
    /// </summary>
    public class BossAgent : BaseAgent
    {
        /// <summary>
        /// A thread-safe repository of high-level organizational knowledge and capabilities.
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _companySkills = new();

        /// <summary>
        /// A background timer used for autonomous oversight and periodic health checks.
        /// </summary>
        private readonly Timer _scheduler;

        /// <summary>
        /// Initializes a new instance of the <see cref="BossAgent"/>.
        /// Sets up a recurring 30-second heartbeat for autonomous company oversight.
        /// </summary>
        public BossAgent(AgentConfig config, IAgentBus bus, DragonMemory memory)
            : base(config, bus, memory)
        {
            // Initializes a strategic pulse: runs every 30 seconds to evaluate the state of the "Company"
            _scheduler = new Timer(async _ => await ExecuteScheduledTasks(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        /// <summary>
        /// Autonomous Oversight: Periodically reviews project status and company health.
        /// This is the 'Pre-emptive' thinking layer of the Boss.
        /// </summary>
        private async Task ExecuteScheduledTasks()
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"[Boss {Config.Name}] Running scheduled company health check...");
            Console.ResetColor();

            // FUTURE: Integration with a "Global Audit" tool to check for deadlocks or resource leaks.
            await Task.CompletedTask;
        }

        /// <summary>
        /// Handles strategic requests from the AgentBus. 
        /// Specifically processes 'DecisionRequest' types where a subordinate agent 
        /// requires executive approval or high-level direction.
        /// </summary>
        /// <param name="message">The incoming message from the Bus.</param>
        protected override async Task ProcessIncomingMessageAsync(AgentMessage message)
        {
            if (message.Type == MessageType.DecisionRequest)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"[Boss {Config.Name}] Received Executive Decision Request from {message.SenderId}");
                Console.ResetColor();

                // 1. Context Gathering: Get the current project map/ASCII graph from the LiveCache
                string projectState = Memory.PersistentCache?.BuildAsciiGraph() ?? "No project map available.";

                // 2. Strategic Prompting: Frame the decision within the context of the entire project
                string prompt = $"Project Architecture Context:\n{projectState}\n\nStrategic Decision Required: {message.Content}";

                // 3. Executive Inference: Use the LLM Provider to weigh options
                var response = await Provider.GenerateResponseAsync(new List<IChatMessage>
                {
                    new ChatMessage("user", prompt)
                });

                // 4. Dispatch Result: Send the decision back to the requester via the Bus
                string decision = response.Content ?? "Executive Decision: Proceed with caution, no specific direction reached.";

                await Bus.SendAsync(new AgentMessage(Config.Id, message.SenderId, MessageType.DecisionResponse, decision, DateTime.Now));
            }
        }

        /// <summary>
        /// Registers a new high-level skill or piece of institutional knowledge.
        /// Knowledge is stored in local dictionary and propagated to the Agent's DragonMemory.
        /// </summary>
        /// <param name="skillName">The identifier for the skill (e.g., 'CudaOptimizationStandards').</param>
        /// <param name="skillData">The descriptive content or instructions for the skill.</param>
        public void StoreCompanySkill(string skillName, string skillData)
        {
            _companySkills[skillName] = skillData;

            // Remember internally - propagate: false because this is top-down information
            Memory.Remember(skillName, skillData, propagate: false);

            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.WriteLine($"[Boss {Config.Name}] Company successfully acquired new institutional skill: {skillName}");
            Console.ResetColor();
        }
    }
}