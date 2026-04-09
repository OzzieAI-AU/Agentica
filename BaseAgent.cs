namespace OzzieAI.Agentica
{
    using OzzieAI.Agentica.Providers;
    using OzzieAI.Agentica.Tools;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// Abstract base class that serves as the foundation for all agents in the OzzieAI.Agentica framework.
    /// Provides rich console logging, tool execution management, conversation memory handling,
    /// and a built-in reasoning loop with circuit breaker protection against infinite loops.
    /// </summary>
    public abstract class BaseAgent
    {

        /// <summary>
        /// List of tools registered with this agent.
        /// </summary>
        protected readonly List<IAgentTool> _tools = new();

        /// <summary>
        /// Tracks the current number of reasoning steps taken in the current task.
        /// Used to prevent infinite loops.
        /// </summary>
        protected int _currentStepCount = 0;

        /// <summary>
        /// Maximum number of reasoning steps allowed before triggering the circuit breaker.
        /// Increased to support more complex tasks such as code fixing or multi-step problem solving.
        /// </summary>
        private readonly int _maxSteps = 15;

        /// <summary>
        /// Configuration settings for this agent (name, ID, provider, etc.).
        /// </summary>
        public AgentConfig Config { get; }

        /// <summary>
        /// Message bus used for communication between agents.
        /// </summary>
        public IAgentBus Bus { get; }

        /// <summary>
        /// Long-term and short-term memory for the agent, storing conversation history.
        /// </summary>
        public DragonMemory Memory { get; }

        /// <summary>
        /// The LLM provider (Grok, Gemini, etc.) used by this agent to generate responses.
        /// </summary>
        public ILlmProvider Provider { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseAgent"/> class.
        /// </summary>
        /// <param name="config">The configuration object for this agent.</param>
        /// <param name="bus">The agent message bus for inter-agent communication.</param>
        /// <param name="memory">The agent's memory instance for maintaining conversation history.</param>
        /// <exception cref="ArgumentNullException">Thrown when any required dependency is null.</exception>
        protected BaseAgent(AgentConfig config, IAgentBus bus, DragonMemory memory)
        {
        
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Bus = bus ?? throw new ArgumentNullException(nameof(bus));
            Memory = memory ?? throw new ArgumentNullException(nameof(memory));
            Provider = config.Provider ?? throw new ArgumentNullException(nameof(config.Provider));

            // Register this agent with the message bus
            Bus.RegisterAgent(Config.Id);
        }

        /// <summary>
        /// Starts listening for incoming messages on the agent's dedicated channel in the message bus.
        /// Each message is processed asynchronously on a background task.
        /// </summary>
        public virtual async Task ListenAsync()
        {
            
            var reader = Bus.GetReader(Config.Id);

            // Continuously read messages as they arrive
            await foreach (var message in reader.ReadAllAsync())
            {
                // Fire-and-forget processing on a separate task to avoid blocking the reader
                _ = Task.Run(async () => await ProcessIncomingMessageAsync(message));
            }
        }

        /// <summary>
        /// When overridden in a derived class, processes incoming messages from other agents or the system.
        /// </summary>
        /// <param name="message">The incoming agent message to process.</param>
        protected abstract Task ProcessIncomingMessageAsync(AgentMessage message);

        /// <summary>
        /// Registers a new tool with this agent, making it available for the LLM to call.
        /// </summary>
        /// <param name="tool">The tool instance to register.</param>
        public void AddTool(IAgentTool tool) => _tools.Add(tool);

        /// <summary>
        /// Starts a new task by adding the user prompt to memory and beginning the reasoning loop.
        /// </summary>
        /// <param name="prompt">The initial user prompt or task description.</param>
        public async Task StartTaskAsync(string prompt)
        {

            ConsoleLogger.WriteLine($"[{Config.Name}] 🚀 Starting new task: {prompt}", ConsoleColor.Blue);
            
            // Reset step counter for the new task
            _currentStepCount = 0;

            // Add the user's prompt to the conversation memory
            Memory.AddMessage(new ChatMessage { Role = "user", Content = prompt });

            // Begin the agent's reasoning loop
            await RunNextStepAsync();
        }

        /// <summary>
        /// Processes the response returned by the LLM provider.
        /// Handles both final text output and tool calls, with rich console logging.
        /// </summary>
        /// <param name="response">The response received from the LLM.</param>
        protected async Task ProcessResponseAsync(IChatResponse response)
        {
            ConsoleLogger.WriteLine($"[DEBUG {Config.Name}] Processing response → Content: {(response.Content?.Length ?? 0)} chars, Tools: {response.ToolCalls?.Count ?? 0}", ConsoleColor.White);

            // Handle textual response from the model (the agent's "thought")
            if (!string.IsNullOrEmpty(response.Content))
            {

                ConsoleLogger.WriteLine($"[{Config.Name}] 💭 Thought: {response.Content}", ConsoleColor.White);

                // Store the assistant's thought in memory
                Memory.AddMessage(new ChatMessage("assistant", response.Content));
            }

            // Handle tool calls requested by the model
            if (response.ToolCalls != null && response.ToolCalls.Count > 0)
            {
                // Circuit breaker: prevent infinite reasoning loops
                if (_currentStepCount >= _maxSteps)
                {

                    ConsoleLogger.WriteLine($"[FATAL {Config.Name}] Circuit breaker triggered after {_maxSteps} steps.", ConsoleColor.Red);
                    
                    Memory.AddMessage(new ChatMessage("assistant", "Failed: Maximum steps reached to prevent infinite loop."));
                    return;
                }

                _currentStepCount++;

                foreach (var call in response.ToolCalls)
                {

                    ConsoleLogger.WriteLine($"[{Config.Name}] 🔧 Calling tool: {call.ToolName}", ConsoleColor.DarkCyan);
                    
                    var tool = _tools.Find(t => t.Name == call.ToolName);

                    if (tool != null)
                    {
                        try
                        {
                            // Execute the tool with the provided arguments
                            string result = await tool.ExecuteAsync(call.ArgumentsJson);

                            ConsoleLogger.WriteLine($"[{Config.Name}] ✅ Tool '{call.ToolName}' returned ({result.Length} chars)", ConsoleColor.Green);
                            
                            // Store the tool result in memory for the next reasoning step
                            Memory.AddMessage(new ChatMessage("tool", result)
                            {
                                ToolId = call.Id,
                                ToolName = call.ToolName
                            });
                        }
                        catch (Exception ex)
                        {
                            ConsoleLogger.WriteLine($"[{Config.Name}] ❌ Tool '{call.ToolName}' failed: {ex.Message}", ConsoleColor.Red);
                         
                            // Store the error as a tool response so the LLM can react to it
                            Memory.AddMessage(new ChatMessage("tool", $"Error: {ex.Message}")
                            {
                                ToolId = call.Id,
                                ToolName = call.ToolName
                            });
                        }
                    }
                    else
                    {

                        ConsoleLogger.WriteLine($"[{Config.Name}] ⚠️ Unknown tool requested: {call.ToolName}", ConsoleColor.Red);
                    }
                }

                ConsoleLogger.WriteLine($"[DEBUG {Config.Name}] Step {_currentStepCount}/{_maxSteps} complete → Continuing reasoning...", ConsoleColor.Blue);

                // Continue the reasoning loop after tool execution
                await RunNextStepAsync();
            }
            else
            {
                // No more tool calls — the agent has reached a final answer:
                ConsoleLogger.WriteLine($"[{Config.Name}] 🏁 No more tools needed. Task reasoning complete.", ConsoleColor.Green);
            }
        }

        /// <summary>
        /// Requests the next reasoning step from the LLM using the current memory history and available tools.
        /// </summary>
        protected async Task RunNextStepAsync()
        {
            ConsoleLogger.WriteLine($"[DEBUG {Config.Name}] Awaiting LLM inference...", ConsoleColor.Magenta);

            try
            {
                // Generate the next response from the LLM
                var response = await Provider.GenerateResponseAsync(Memory.History, _tools);

                // Process the LLM's response (thoughts and/or tool calls)
                await ProcessResponseAsync(response);
            }
            catch (Exception ex)
            {

                ConsoleLogger.WriteLine($"[CRITICAL {Config.Name}] LLM inference failed: {ex.Message}", ConsoleColor.Red);
            }
        }
    }
}