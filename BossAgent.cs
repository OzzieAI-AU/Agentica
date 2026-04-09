namespace OzzieAI.Agentica
{
    using OzzieAI.Agentica.Providers;
    using OzzieAI.Agentica.Tools;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// ✨ THE BOSS - Fully Autonomous Swarm Orchestrator ✨
    /// Top-level agent responsible for receiving high-level missions, dynamically creating and managing
    /// Manager and Worker agents, assigning tasks, making executive decisions, and maintaining company-wide skills.
    /// Features nice agent naming, startup throttling to protect local models (e.g. Ollama), and rich logging.
    /// </summary>
    public class BossAgent : BaseAgent
    {
        /// <summary>
        /// Thread-safe dictionary storing company-wide skills and knowledge.
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _companySkills = new();

        /// <summary>
        /// Timer used to run periodic scheduled tasks / heartbeat checks.
        /// </summary>
        private readonly Timer _scheduler;

        /// <summary>
        /// Dictionary of available LLM providers (brains) that can be assigned to newly created agents.
        /// Key is a friendly name (e.g. "grok", "ollama"), value is the concrete provider instance.
        /// </summary>
        private readonly Dictionary<string, ILlmProvider> _availableBrains;

        /// <summary>
        /// Reference to the currently active ManagerAgent (if any).
        /// </summary>
        private ManagerAgent? _currentManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="BossAgent"/> class.
        /// </summary>
        /// <param name="config">Configuration for the Boss agent.</param>
        /// <param name="bus">The agent message bus for inter-agent communication.</param>
        /// <param name="memory">Shared memory instance for the swarm.</param>
        /// <param name="availableBrains">Dictionary of available LLM providers that can be assigned to child agents.</param>
        /// <exception cref="ArgumentNullException">Thrown when availableBrains is null.</exception>
        public BossAgent(AgentConfig config, IAgentBus bus, DragonMemory memory,
                         Dictionary<string, ILlmProvider> availableBrains)
            : base(config, bus, memory)
        {
            _availableBrains = availableBrains ?? throw new ArgumentNullException(nameof(availableBrains));

            // Start a recurring timer for scheduled maintenance / heartbeat tasks
            _scheduler = new Timer(async _ => await ExecuteScheduledTasks(), null,
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        /// <summary>
        /// Executes a high-level mission by orchestrating the creation of a Manager and multiple Worker agents.
        /// Assigns meaningful names and throttles worker startup to avoid overwhelming local LLM instances.
        /// </summary>
        /// <param name="mission">The high-level mission or goal to accomplish.</param>
        public async Task ExecuteHighLevelMissionAsync(string mission)
        {

            ConsoleLogger.WriteLine($"[Boss {Config.Name}] Received mission: {mission}", ConsoleColor.Cyan);

            // Simple safe defaults (can be improved with LLM-based planning later)
            int numManagers = 1;
            int numWorkers = 3;

            // 1. Create and start the Manager agent
            var manager = StartManager("ResearchManager", _availableBrains["grok"], Memory);
            _currentManager = manager;

            // 2. Create Worker agents with throttling and friendly names
            ConsoleLogger.WriteLine($"[Boss {Config.Name}] Starting {numWorkers} workers with delay to protect Ollama...", ConsoleColor.Cyan);

            for (int i = 1; i <= numWorkers; i++)
            {
                await Task.Delay(800); // Throttle startup to prevent resource overload

                string workerName = i == 1 ? "Researcher" :
                                   (i == 2 ? "Coder" : "Builder");

                var worker = StartWorker(workerName, _availableBrains["ollama"], Memory);

                // Immediately assign a portion of the mission to the new worker
                await Bus.SendAsync(new AgentMessage(
                    Config.Id,
                    worker.Config.Id,
                    MessageType.TaskAssignment,
                    $"Contribute to mission: {mission}. You are Worker {i} ({workerName}).",
                    DateTime.UtcNow));
            }

            ConsoleLogger.WriteLine($"[Boss {Config.Name}] Swarm fully deployed with nice names and throttling.", ConsoleColor.Green);
        }

        /// <summary>
        /// Creates and starts a new ManagerAgent with a given name and LLM provider.
        /// </summary>
        /// <param name="managerName">Friendly name for the manager (e.g. "ResearchManager").</param>
        /// <param name="provider">The LLM provider to assign to this manager.</param>
        /// <param name="memory">The memory instance to share with the manager.</param>
        /// <returns>The newly created and registered ManagerAgent.</returns>
        public ManagerAgent StartManager(string managerName, ILlmProvider provider, DragonMemory memory)
        {
            var config = new AgentConfig
            {
                Id = $"Mgr-{managerName}-{Guid.NewGuid().ToString("N").Substring(0, 6)}",
                Name = managerName,
                Provider = provider,
                ParentId = Config.Id
            };

            var manager = new ManagerAgent(config, Bus, memory);
            Bus.RegisterAgent(config.Id);

            // CRITICAL: Start the Manager listening immediately
            _ = Task.Run(async () => await manager.ListenAsync());

            ConsoleLogger.WriteLine($"[Boss {Config.Name}] ✅ Manager started → {managerName} (ID: {config.Id})", ConsoleColor.Cyan);

            return manager;
        }

        /// <summary>
        /// Creates and starts a new WorkerAgent with a given name and LLM provider.
        /// Automatically assigns the worker to the current manager if one exists.
        /// </summary>
        /// <param name="workerName">Friendly name for the worker (e.g. "Researcher", "Coder").</param>
        /// <param name="provider">The LLM provider to assign to this worker.</param>
        /// <param name="memory">The memory instance to share with the worker.</param>
        /// <returns>The newly created and registered WorkerAgent.</returns>
        public WorkerAgent StartWorker(string workerName, ILlmProvider provider, DragonMemory memory)
        {

            var config = new AgentConfig
            {
                Id = $"Wkr-{workerName}-{Guid.NewGuid().ToString("N").Substring(0, 6)}",
                Name = workerName,
                Provider = provider,
                ParentId = Config.Id
            };

            var worker = new WorkerAgent(config, Bus, memory);

            // === TOOL REGISTRATION - Give the worker its "Hands" ===
            worker.AddTool(new WebSearchTool());           // Eyes: Can search the internet
            worker.AddTool(new FileToolExecutor());        // Hands: Read/Write files
            worker.AddTool(new TerminalTool());            // Action: Run commands (build, etc.)
            worker.AddTool(new CodeSafetyGateTool());      // Guard: Validates C# code before saving
                                                           // You can add more tools here later (ApprovalTool, etc.)

            Bus.RegisterAgent(config.Id);

            if (_currentManager != null)
                _currentManager.AssignWorker(config.Id);

            // Start listening so the worker can receive tasks
            _ = Task.Run(async () => await worker.ListenAsync());

            ConsoleLogger.WriteLine($"[Boss {Config.Name}] ✅ Worker started → {workerName} (ID: {config.Id}) with tools loaded", ConsoleColor.Cyan);

            return worker;
        }

        /// <summary>
        /// Executes periodic scheduled tasks (currently just a heartbeat log).
        /// Triggered by the recurring Timer.
        /// </summary>
        private async Task ExecuteScheduledTasks()
        {

            ConsoleLogger.WriteLine($"[Boss {Config.Name}] Heartbeat check...", ConsoleColor.DarkMagenta);

            await Task.CompletedTask;
        }

        /// <summary>
        /// Processes incoming messages directed to the Boss agent.
        /// Currently handles DecisionRequest messages by generating executive decisions.
        /// </summary>
        /// <param name="message">The incoming agent message.</param>
        protected override async Task ProcessIncomingMessageAsync(AgentMessage message)
        {
            if (message.Type == MessageType.DecisionRequest)
            {

                ConsoleLogger.WriteLine($"[Boss {Config.Name}] Executive Decision Request from {message.SenderId}", ConsoleColor.White);
                
                // Build project context from memory
                string projectState = Memory.PersistentCache?.BuildAsciiGraph() ?? "No project map.";

                string prompt = $"Project Context:\n{projectState}\n\nDecision needed: {message.Content}";

                // Ask the Boss's own LLM provider for a decision
                var response = await Provider.GenerateResponseAsync(new List<IChatMessage>
                {
                    new ChatMessage("user", prompt)
                });

                string decision = response.Content ?? "Proceed with caution.";

                // Send the decision back to the requester
                await Bus.SendAsync(new AgentMessage(Config.Id, message.SenderId,
                    MessageType.DecisionResponse, decision, DateTime.Now));
            }
        }

        /// <summary>
        /// Stores a company-wide skill or piece of knowledge in both local storage and shared memory.
        /// </summary>
        /// <param name="skillName">Name/identifier of the skill.</param>
        /// <param name="skillData">The actual skill content or data.</param>
        public void StoreCompanySkill(string skillName, string skillData)
        {
            _companySkills[skillName] = skillData;

            // Store in memory without propagating (company-level knowledge)
            Memory.Remember(skillName, skillData, propagate: false);
        }
    }
}