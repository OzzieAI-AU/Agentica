namespace OzzieAI.Agentica.Agents
{
    using OzzieAI.Agentica;
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


        public LiveCache LiveCache => _sharedCache;

        /// <summary>
        /// Initializes a new instance of the <see cref="AgentFactory"/>.
        /// </summary>
        /// <param name="bus">The global communication bus for all agents.</param>
        /// <param name="liveCache">The persistent project-level cache for RAG and file tracking.</param>
        public AgentFactory(IAgentBus bus, LiveCache liveCache)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _sharedCache = liveCache ?? throw new ArgumentNullException(nameof(liveCache));
        }

        /// <summary>
        /// Creates the Boss agent and starts its listening loop.
        /// The Boss will then autonomously create Managers and Workers.
        /// </summary>
        public BossAgent CreateBoss(AgentConfig config, Dictionary<string, ILlmProvider> availableBrains)
        {

            // 1. Beautify the Boss ID to match the swarm pattern
            config.Id = config.BossId;

            // 2. REGISTER the Boss so it can receive TaskResults
            _bus.RegisterAgent(config.Id);

            DragonMemory memory = new DragonMemory(config.BossId, _sharedCache);
            var boss = new BossAgent(config, _bus, memory, availableBrains);

            // 3. START the listening loop
            _ = Task.Run(() => boss.ListenAsync(config));

            return _currentBoss = boss;
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
                AgentRole.Manager => CreateManager(config),
                AgentRole.Worker => CreateWorker(config),
                _ => throw new ArgumentException($"[FACTORY ERROR] Unknown role: {config.Role}")
            };

            // 2. Attach the Global LiveCache for persistent project awareness
            agent.Memory.AttachPersistentCache(_sharedCache);

            // 3. Activate the Agent's non-blocking listener
            // This allows the agent to begin receiving messages on the Bus immediately.
            _ = Task.Run(() => agent.ListenAsync(config));

            ConsoleLogger.WriteLine($"[FACTORY] Successfully deployed {config.Name} as {config.Role}.", ConsoleColor.Gray);

            return agent;
        }

        /// <summary>
        /// Internal: Spawns a Manager and wires its memory upstream to the Boss.
        /// </summary>
        private ManagerAgent CreateManager(AgentConfig config)
        {
            // 
            DragonMemory ManagerMemory = new DragonMemory(config.Id, _sharedCache);
            
            // 1. Ensure the Manager knows who its Boss is BEFORE instantiation
            if (_currentBoss != null)
            {
                config.ManagerId = _currentBoss.Config.Id;
            }

            var manager = new ManagerAgent(config, _bus, ManagerMemory);

            // 2. Cognitive Bonding: Manager learns from the Boss's strategic decisions
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
            // 
            DragonMemory WorkerMemory = new DragonMemory(config.Id, _sharedCache);

            // 1. Hierarchical Wiring: Assign the direct Parent/Boss ID
            if (_currentManager != null)
            {
                config.ManagerId = _currentManager.Config.Id;
            }
            else if (_currentBoss != null)
            {
                config.ManagerId = _currentBoss.Config.Id;
            }

            var worker = new WorkerAgent(config, _bus, WorkerMemory);

            // 2. Cognitive Bonding: Establish memory inheritance
            if (_currentManager != null)
                worker.Memory.AddUpstream(_currentManager.Memory);
            else if (_currentBoss != null)
                worker.Memory.AddUpstream(_currentBoss.Memory);

            return worker;
        }
    }
}