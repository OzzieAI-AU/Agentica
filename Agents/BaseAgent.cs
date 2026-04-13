namespace OzzieAI.Agentica
{
    using OzzieAI.Agentica.Agents;
    // ─────────────────────────────────────────────────────────────────────────────
    // NAMESPACE DOCUMENTATION
    // ─────────────────────────────────────────────────────────────────────────────
    // This namespace encapsulates the entire Agentica framework — a production-grade,
    // swarm-native AI agent architecture built for OzzieAI. Every class, tool, and
    // provider lives here to guarantee zero naming collisions and crystal-clear
    // architectural boundaries. The design philosophy is explicit, observable,
    // and relentlessly resilient.

    using OzzieAI.Agentica.Providers;      // All LLM provider abstractions (OpenAI, Anthropic, Grok, etc.)
    using OzzieAI.Agentica.Tools;          // The IAgentTool contract and all concrete tool implementations
    using System;                          // Core .NET primitives: Exception, ArgumentNullException, etc.
    using System.Collections.Generic;      // Generic collections required for tool lists and message history
    using System.Threading.Tasks;          // The entire async/await ecosystem that powers non-blocking agent life-cycles

    /// <summary>
    /// BaseAgent — The immutable foundation stone of every agent in the OzzieAI swarm.
    /// 
    /// This abstract class implements the exact Think → Act → Observe cycle described in
    /// ReadMe.txt Section II. It guarantees that every derived agent inherits:
    ///   • Non-blocking, fire-and-forget message listening
    ///   • Full try/catch isolation so a single agent can never bring down the swarm
    ///   • Automatic memory management via DragonMemory
    ///   • Tool orchestration and response persistence
    ///   • Hard safety limits (MaxSteps) to prevent infinite reasoning loops
    /// 
    /// All concrete agents (Researcher, Executor, Critic, Planner, etc.) inherit from
    /// this class, ensuring the swarm is self-healing, observable, and immortal.
    /// </summary>
    public abstract class BaseAgent
    {
        // ─────────────────────────────────────────────────────────────────────
        // PROTECTED READONLY DEPENDENCIES
        // These fields are deliberately immutable after construction to enforce
        // architectural integrity and thread-safety guarantees.
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Immutable configuration object containing agent identity, LLM provider,
        /// and swarm-specific settings. Never null after construction.
        /// </summary>
        public AgentConfig Config { get; set; }

        /// <summary>
        /// The message bus that enables zero-copy, in-memory communication between
        /// all agents in the swarm. Acts as the nervous system of the collective.
        /// </summary>
        public IAgentBus Bus { get; set; }

        /// <summary>
        /// Long-term and short-term memory engine (DragonMemory) that persists
        /// conversation history, tool results, and observations across the agent's lifetime.
        /// </summary>
        public DragonMemory Memory { get; set; }

        /// <summary>
        /// The concrete LLM provider selected for this agent. Resolved from Config.Provider
        /// at construction time. All model calls are routed through this abstraction.
        /// </summary>
        public ILlmProvider Provider { get; set; }

        /// <summary>
        /// Thread-safe collection of tools this agent is permitted to use.
        /// Populated via the fluent AddTool() method after construction.
        /// </summary>
        public List<IAgentTool> _tools = new();

        // ─────────────────────────────────────────────────────────────────────
        // PRIVATE CONSTANTS
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Hard safety limit on reasoning steps per task. Prevents runaway token
        /// consumption and guarantees agents eventually reach a terminal state.
        /// Value deliberately chosen to balance depth with cost control.
        /// </summary>
        private const int MaxSteps = 15;

        // ─────────────────────────────────────────────────────────────────────
        // CONSTRUCTOR
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Protected constructor enforces dependency injection and null-safety.
        /// Every derived agent must call : base(...) and pass valid instances.
        /// </summary>
        /// <param name="config">Fully populated AgentConfig for this agent instance.</param>
        /// <param name="bus">Reference to the shared swarm message bus.</param>
        /// <param name="memory">DragonMemory instance that will own this agent's history.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when any required dependency is null, failing fast with a crystal-clear message.
        /// </exception>
        protected BaseAgent(AgentConfig config, IAgentBus bus, DragonMemory memory)
        {
            // 1. Guard clause: config is mandatory
            Config = config ?? throw new ArgumentNullException(nameof(config));

            // 2. Guard clause: bus is mandatory
            Bus = bus ?? throw new ArgumentNullException(nameof(bus));

            // 3. Guard clause: memory is mandatory
            Memory = memory ?? throw new ArgumentNullException(nameof(memory));

            // 4. Extract and validate the LLM provider from configuration
            Provider = config.Provider ?? throw new ArgumentNullException(nameof(config.Provider));
        }

        // ─────────────────────────────────────────────────────────────────────
        // TOOL REGISTRATION
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Fluent method that registers a tool with this agent. Called during
        /// swarm bootstrapping to compose agent capabilities dynamically.
        /// </summary>
        /// <param name="tool">Any implementation of IAgentTool.</param>
        public void AddTool(IAgentTool tool) => _tools.Add(tool);

        // ─────────────────────────────────────────────────────────────────────
        // LISTEN LIFECYCLE (THE HEART OF THE SWARM)
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Starts the agent's eternal listening loop on its dedicated message channel.
        /// This method is fire-and-forget and never completes unless the bus itself is disposed.
        /// Every incoming message is processed on a separate Task to guarantee non-blocking behavior.
        /// </summary>
        public async Task ListenAsync(AgentConfig config)
        {

            //Config = config;
            var reader = Bus.GetReader(config.Id);

            ConsoleLogger.WriteLine($"[Agent {config.Name}] 👂 Listening started on channel {config.Id}", ConsoleColor.DarkGray);

            await foreach (var message in reader.ReadAllAsync())
            {
                // One task per message to ensure sequential "Process -> Think" logic
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // 1. Process: Handle routing, state updates, or specialized logic
                        await ProcessIncomingMessageAsync(message);

                        // 2. Think: Trigger the actual LLM reasoning cycle (The Brain)
                        await ThinkActObserveAsync(message);
                    }
                    catch (Exception ex)
                    {
                        ConsoleLogger.WriteLine($"[Agent {config.Name}] 💥 CRITICAL ERROR: {ex.Message}", ConsoleColor.Red);
                    }
                });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // ABSTRACT MESSAGE PROCESSOR
        // Derived agents implement their unique intelligence here.
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Abstract entry point for every incoming swarm message.
        /// Concrete agents override this to decide whether to ignore, delegate,
        /// or kick off a new reasoning cycle via StartTaskAsync().
        /// </summary>
        /// <param name="message">The deserialized AgentMessage from the bus.</param>
        protected abstract Task ProcessIncomingMessageAsync(AgentMessage message);

        /// <summary>
        /// Executes the LLM request with Mandatory Exponential Backoff (1s, 2s, 4s, 8s, 16s).
        /// </summary>
        protected async Task<LlmResponse?> GenerateResponseWithRetryAsync()
        {
            int maxRetries = 5;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    var response = await Config.Provider.GenerateResponseAsync(Memory.History, _tools);
                    return (LlmResponse)response;
                }
                catch (Exception ex) when (i < maxRetries - 1)
                {
                    int delay = (int)Math.Pow(2, i) * 1000;
                    // Do not log to console as per instructions, just wait
                    await Task.Delay(delay);
                }
                catch (Exception finalEx)
                {
                    ConsoleLogger.WriteLine($"[{Config.Name}] 🛑 Permanent LLM Failure after {maxRetries} attempts: {finalEx.Message}", ConsoleColor.Red);
                }
            }
            return null;
        }

        protected virtual async Task ThinkActObserveAsync(AgentMessage message)
        {
            // 1. RECALL KNOWLEDGE
            string? relevantSkill = Memory.Recall(message.Content);
            if (!string.IsNullOrEmpty(relevantSkill))
            {
                Memory.History.Add(new ChatMessage("system", $"[RECALLED KNOWLEDGE]: {relevantSkill}"));
            }

            Memory.History.Add(new ChatMessage("user", message.Content));

            // 2. CONTEXT CONCILIATION (Summary block logic)
            if (Memory.History.Count > 10)
            {
                var manager = new MemoryManager();
                var condensed = await manager.ConciliateMemoryAsync(
                    Memory.History.ConvertAll(m => (ChatMessage)m),
                    Config.Provider as OzzieAI.Agentica.Providers.Ollama.OllamaProvider);

                Memory.History.Clear();
                Memory.History.AddRange(condensed);
            }

            // 3. GENERATE RESPONSE WITH BACKOFF
            var response = await GenerateResponseWithRetryAsync();

            if (response != null)
            {
                await ProcessResponseAsync(response);
            }
            else
            {
                // Notify Manager of technical failure so it doesn't loop on "Parsing Error"
                await Bus.SendAsync(new AgentMessage(
                    Config.Id,
                    message.SenderId,
                    MessageType.TaskResult,
                    "ERROR: LLM_TIMEOUT_RETRY_EXHAUSTED. Please check local Ollama instance.",
                    DateTime.UtcNow));
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // CORE THINK-ACT-OBSERVE CYCLE
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Orchestrates the complete reasoning loop for a given task description.
        /// Clears prior memory, injects a fresh system prompt, then iterates
        /// up to MaxSteps of: LLM → Tool Execution → Memory Persistence.
        /// </summary>
        /// <param name="taskDescription">Human-readable goal for this reasoning session.</param>
        protected async Task StartTaskAsync(string taskDescription)
        {

            // Fix: Always clear and rebuild the system prompt using the LATEST Config
            Memory.History.Clear();

            // Explicitly pull the BossId from the current state of Config
            string bossInfo = !string.IsNullOrEmpty(Config.BossId) ? $" Your Boss is {Config.BossId}." : "";

            // 2. Inject the authoritative system prompt that defines the agent's persona and mission
            Memory.History.Add(new ChatMessage("system", $"You are {Config.Name}. ID: {Config.Id}.{bossInfo} Task: {taskDescription}"));

            // 3. Initialize step counter for the safety limit
            int step = 0;

            // 4. Enter the main reasoning loop (the "Think-Act" heartbeat)
            while (step < MaxSteps)
            {
                // 5. Increment and log the current step for observability
                step++;
                ConsoleLogger.WriteLine($"[DEBUG {Config.Name}] Step {step}/{MaxSteps} complete ␦ Continuing reasoning...", ConsoleColor.DarkGray);

                // 6. Ask the LLM for the next response (this is the "Think" phase)
                LlmResponse response = ((LlmResponse)await Provider.GenerateResponseAsync(Memory.History, _tools));

                // 7. Process the LLM's output — this may trigger tool calls or final answer
                await ProcessResponseAsync(response);

                // 8. Termination condition: if the LLM returned no tool calls, reasoning is complete
                if (response.ToolCalls == null || response.ToolCalls.Count == 0)
                {
                    ConsoleLogger.WriteLine($"[DEBUG {Config.Name}] No more tools needed. Task reasoning complete.", ConsoleColor.DarkGray);
                    break;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // RESPONSE PROCESSOR (ACT + OBSERVE)
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Handles the LLM's response by persisting content to memory and executing
        /// any requested tool calls. This is the "Act" and "Observe" phase of the cycle.
        /// </summary>
        /// <param name="response">The structured response returned by the LLM provider.</param>
        private async Task ProcessResponseAsync(LlmResponse response)
        {
            // 1. If the LLM produced natural language output, persist it as an assistant message
            if (!string.IsNullOrEmpty(response.Content))
                Memory.History.Add(new ChatMessage("assistant", response.Content));

            // 2. If the LLM requested tool execution, iterate through every tool call
            if (response.ToolCalls?.Count > 0)
            {
                foreach (var toolCall in response.ToolCalls)
                {
                    // 3. Resolve the tool by name (O(1) lookup via FirstOrDefault)
                    var tool = _tools.FirstOrDefault(t => t.Name == toolCall.ToolName);

                    if (tool != null)
                    {
                        // 4. Execute the tool asynchronously (this is the "Act" phase)
                        string result = await tool.ExecuteAsync(toolCall.ArgumentsJson);

                        // 5. Persist the tool result back into memory for the next reasoning iteration
                        //    (this is the "Observe" phase)
                        Memory.History.Add(new ChatMessage("tool", result, toolCall.ToolName));
                    }
                }
            }
        }

        /// <summary>
        /// The primary cognitive loop. Optimised to perform Context Conciliation 
        /// and Tactical Recall before engaging the LLM.
        /// </summary>
        public async Task ThinkAsync(AgentMessage message)
        {
            // 1. Initialise Memory with the incoming task if history is empty
            if (Memory.History.Count == 0)
            {
                Memory.History.Add(new ChatMessage("system", Config.TaskDescription));
            }

            // 2. TACTICAL RECALL: Check if we have learned skills relevant to this content
            // This transforms the agent from 'forgetful' to 'expert'.
            string? relevantSkill = Memory.Recall(message.Content);
            if (!string.IsNullOrEmpty(relevantSkill))
            {
                Memory.History.Add(new ChatMessage("system", $"[RECALLED KNOWLEDGE]: {relevantSkill}"));
            }

            Memory.History.Add(new ChatMessage("user", message.Content));

            // 3. CONTEXT CONCILIATION: Prevent Token Overflow
            // We use the MemoryManager to summarize middle-history while keeping the Anchor and the Latest context.
            if (Memory.History.Count > 10)
            {
                var manager = new MemoryManager();
                // Note: In production, use a fast/cheap model for summarization
                var condensed = await manager.ConciliateMemoryAsync(
                    Memory.History.ConvertAll(m => (ChatMessage)m),
                    Config.Provider as OzzieAI.Agentica.Providers.Ollama.OllamaProvider);

                Memory.History.Clear();
                Memory.History.AddRange(condensed);
            }

            // 4. GENERATE RESPONSE
            var response = await Config.Provider.GenerateResponseAsync(Memory.History, _tools);

            // 5. PROCESS & PERSIST (Handles tool execution and result storage)
            await ProcessResponseAsync((LlmResponse)response);
        }
    }
}