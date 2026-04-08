namespace OzzieAI.Agentica
{
    using OzzieAI.Agentica.Providers;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Acts as the tactical orchestrator within the Agentica hierarchy.
    /// The Manager is responsible for delegating tasks from the Boss to specialized Workers, 
    /// aggregating results, and performing knowledge synthesis.
    /// </summary>
    public class ManagerAgent : BaseAgent
    {
        /// <summary>
        /// A thread-safe collection of unique identifiers for Workers under this Manager's supervision.
        /// </summary>
        private readonly ConcurrentBag<string> _workerIds = new();

        /// <summary>
        /// A local repository of specialized operational skills or 'How-To' guides 
        /// learned during the project lifecycle.
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _skills = new();

        /// <summary>
        /// Initializes a new Manager Agent with full cognitive and communication infrastructure.
        /// </summary>
        public ManagerAgent(AgentConfig config, IAgentBus bus, DragonMemory memory)
            : base(config, bus, memory) { }

        /// <summary>
        /// Registers a Worker's ID into the Manager's tactical pool for task delegation.
        /// </summary>
        public void AssignWorker(string workerId) => _workerIds.Add(workerId);

        /// <summary>
        /// The reactive core of the Manager. Processes hierarchical signals from the Boss 
        /// (top-down) and status reports from Workers (bottom-up).
        /// </summary>
        /// <param name="message">The incoming inter-agent message from the <see cref="IAgentBus"/>.</param>
        protected override async Task ProcessIncomingMessageAsync(AgentMessage message)
        {

            // --- CRITICAL MEMORY INGESTION ---
            // Ensure the Manager's history is initialized and the current interaction is logged.
            // Without this, the Manager 'forgets' who it is talking to or what it was asked to do.
            // Memory.History ??= new List<IChatMessage>();
            Memory.History.Add(new ChatMessage
            {
                Role = message.Type == MessageType.TaskResult ? "user" : "assistant",
                Content = $"Received {message.Type} from {message.SenderId}: {message.Content}"
            });

            switch (message.Type)
            {
                case MessageType.TaskAssignment:
                    // Logic: Boss (or User) sent a high-level task. 
                    // The Manager breaks it down or routes it to the available workforce.
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine($"[Manager {Config.Name}] Tactical Delegation: Routing task to Worker pool.");
                    Console.ResetColor();
                    await DelegateToWorkerAsync(message.Content);
                    break;

                case MessageType.TaskResult:
                    // Logic: A Worker finished a job. 
                    // The Manager validates/summarizes and prepares a 'Decision Request' for the Boss.
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[Manager {Config.Name}] Quality Assurance: Reviewing Worker output.");
                    Console.ResetColor();

                    await Bus.SendAsync(new AgentMessage(
                        Config.Id,
                        "BOSS_ID", // Typically resolved via Config.ParentId or a Directory Service
                        MessageType.DecisionRequest,
                        $"Worker {message.SenderId} completed task. Summary: {message.Content}",
                        DateTime.Now));
                    break;

                case MessageType.KnowledgeTransfer:
                    // Logic: Capturing new patterns or technical discoveries.
                    var skillKey = $"Skill_{DateTime.UtcNow:yyyyMMdd}_{Guid.NewGuid().ToString()[..4]}";
                    LearnSkill(skillKey, message.Content);
                    break;
            }
        }

        /// <summary>
        /// Implements a routing strategy to dispatch work to sub-agents.
        /// Current implementation: Basic 'First-Available' (TryPeek).
        /// </summary>
        private async Task DelegateToWorkerAsync(string task)
        {
            // In a more advanced version, this would check Worker 'Specialties' via Memory
            if (_workerIds.TryPeek(out var workerId))
            {
                await Bus.SendAsync(new AgentMessage(Config.Id, workerId, MessageType.TaskAssignment, task, DateTime.Now));
            }
            else
            {
                Console.WriteLine($"[WARNING] Manager {Config.Name} has no workers assigned to handle: {task}");
            }
        }

        /// <summary>
        /// Updates the Manager's internal knowledge base and propagates the discovery 
        /// up the chain to the Boss via <see cref="DragonMemory"/>.
        /// </summary>
        private void LearnSkill(string skillName, string skillData)
        {
            _skills[skillName] = skillData;

            // Propagation=true ensures the Boss's memory eventually inherits this skill
            Memory.Remember(skillName, skillData, propagate: true);

            Console.WriteLine($"[Manager {Config.Name}] Knowledge Synthesized: {skillName}");
        }
    }
}