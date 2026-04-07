namespace OzzieAI.Agentica
{
    using OzzieAI.Agentica.Providers;
    using OzzieAI.Agentica.Tools;
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// The centralized factory responsible for instantiating and architecturally wiring 
    /// the Agentica workforce. It manages dependency injection for the Bus, Cache, and 
    /// LLM Providers, while establishing the hierarchical memory inheritance between roles.
    /// </summary>
    public class AgentFactory
    {

        private readonly IAgentBus _bus;
        private readonly LiveCache _sharedCache;

        // Tracks existing leadership to facilitate hierarchical memory bonding
        private BossAgent? _currentBoss;
        private ManagerAgent? _currentManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="AgentFactory"/>.
        /// </summary>
        /// <param name="bus">The global communication bus for all agents.</param>
        /// <param name="liveCache">The persistent project-level cache for RAG and file tracking.</param>
        /// <param name="provider">The primary LLM provider (Ollama, Gemini, or Grok).</param>
        public AgentFactory(IAgentBus bus, LiveCache liveCache)
        {
        
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _sharedCache = liveCache ?? throw new ArgumentNullException(nameof(liveCache));
        }

        /// <summary>
        /// Creates, configures, and activates an agent based on the provided configuration.
        /// Handles role-specific logic, memory propagation, and background listener activation.
        /// </summary>
        /// <param name="config">The identity and role definitions for the new agent.</param>
        /// <returns>A fully initialized and 'online' <see cref="BaseAgent"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when an unsupported AgentRole is provided.</exception>
        public BaseAgent CreateAgent(AgentConfig config)
        {
            // 1. Instantiate the specific implementation based on Role
            BaseAgent agent = config.Role switch
            {
                AgentRole.Boss => CreateBoss(config),
                AgentRole.Manager => CreateManager(config),
                AgentRole.Worker => CreateWorker(config),
                _ => throw new ArgumentException($"[FACTORY ERROR] Unknown role: {config.Role}")
            };

            // 2. Attach the Global LiveCache for persistent project awareness
            agent.Memory.AttachPersistentCache(_sharedCache);

            // 3. Activate the Agent's non-blocking listener
            // This allows the agent to begin receiving messages on the Bus immediately.
            _ = Task.Run(() => agent.ListenAsync());

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"[FACTORY] Successfully deployed {config.Name} as {config.Role}.");
            Console.ResetColor();

            return agent;
        }

        /// <summary>
        /// Internal: Spawns the Supreme Executive.
        /// </summary>
        private BossAgent CreateBoss(AgentConfig config)
        {
            return _currentBoss = new BossAgent(config, _bus, new DragonMemory());
        }

        /// <summary>
        /// Internal: Spawns a Manager and wires its memory upstream to the Boss.
        /// </summary>
        private ManagerAgent CreateManager(AgentConfig config)
        {
            var manager = new ManagerAgent(config, _bus, new DragonMemory());

            // Cognitive Bonding: Manager learns from the Boss's strategic decisions
            if (_currentBoss != null)
                manager.Memory.AddUpstream(_currentBoss.Memory);

            return _currentManager = manager;
        }

        /// <summary>
        /// Internal: Spawns a Worker (Bob) and wires its memory upstream to Leadership.
        /// Priority is given to the Manager, otherwise falls back to the Boss.
        /// </summary>
        private WorkerAgent CreateWorker(AgentConfig config)
        {
            var worker = new WorkerAgent(config, _bus, new DragonMemory());

            // Cognitive Bonding: Worker learns from the direct Manager or the Boss
            if (_currentManager != null)
                worker.Memory.AddUpstream(_currentManager.Memory);
            else if (_currentBoss != null)
                worker.Memory.AddUpstream(_currentBoss.Memory);

            return worker;
        }
    }
}