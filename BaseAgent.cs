namespace OzzieAI.Agentica
{
    using OzzieAI.Agentica.Providers;
    using OzzieAI.Agentica.Tools;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// The architectural foundation for all autonomous entities. 
    /// Manages the 'Think-Act-Observe' cycle and provides a non-blocking 
    /// listener for inter-agent communication via the AgentBus.
    /// </summary>
    public abstract class BaseAgent
    {

        /// <summary>
        /// Registry of functional capabilities available to this agent.
        /// </summary>
        protected readonly List<IAgentTool> _tools = new();

        public AgentConfig Config { get; }
        public IAgentBus Bus { get; }
        public DragonMemory Memory { get; }
        public ILlmProvider Provider { get; }

        protected BaseAgent(AgentConfig config, IAgentBus bus, DragonMemory memory)
        {

            Config = config ?? throw new ArgumentNullException(nameof(config));
            Bus = bus ?? throw new ArgumentNullException(nameof(bus));
            Memory = memory ?? throw new ArgumentNullException(nameof(memory));
            Provider = config.Provider ?? throw new ArgumentNullException(nameof(config.Provider));

            // Register this agent on the bus immediately upon initialization
            Bus.RegisterAgent(Config.Id);
        }


        /// <summary>
        /// Starts the background listener. This allows the agent to react to 
        /// messages (Tasks, Decisions, Knowledge) sent by other agents in the hierarchy.
        /// </summary>
        public virtual async Task ListenAsync()
        {
        
            var reader = Bus.GetReader(Config.Id);

            // ReadAllAsync ensures we process messages as they arrive without blocking
            await foreach (var message in reader.ReadAllAsync())
            {
                // Offload to Task.Run to ensure the listener remains responsive 
                // while the agent is 'Thinking'
                _ = Task.Run(() => ProcessIncomingMessageAsync(message));
            }
        }


        /// <summary>
        /// Triggered when a message is received from the Bus. 
        /// Sub-classes define their specific reactive logic here.
        /// </summary>
        protected abstract Task ProcessIncomingMessageAsync(AgentMessage message);


        /// <summary>
        /// Injects a new tool (capability) into the agent's repertoire.
        /// </summary>
        public void AddTool(IAgentTool tool) => _tools.Add(tool);


        /// <summary>
        /// Entry point for a new reasoning thread. 
        /// Adds the prompt to memory and initiates the LLM-driven execution loop.
        /// </summary>
        public async Task StartTaskAsync(string prompt)
        {
            
            Memory.AddMessage(new ChatMessage() 
            {
                Content = prompt, 
                MediaData = null, 
                MimeType = null, 
                Role = "user", 
                ToolId = null, 
                ToolName = null
            });
            await RunNextStepAsync();
        }


        /// <summary>
        /// Evaluates LLM output, executes requested tools, and recurses until the task is complete.
        /// </summary>
        protected async Task ProcessResponseAsync(IChatResponse response)
        {
        
            // 1. Log and Store Reasoning
            if (!string.IsNullOrEmpty(response.Content))
            {
                Console.WriteLine($"[{Config.Name}] {response.Content}");
                Memory.AddMessage(new ChatMessage("assistant", response.Content));
            }

            // 2. Execute Actions (The 'Act' phase)
            if (response.ToolCalls != null && response.ToolCalls.Count > 0)
            {
                foreach (var call in response.ToolCalls)
                {
                    var tool = _tools.Find(t => t.Name == call.ToolName);
                    if (tool != null)
                    {
                        // Execute the actual tool logic (File IO, CLI, etc.)
                        string result = await tool.ExecuteAsync(call.ArgumentsJson);

                        // Feed the result back into memory so the LLM can 'Observe'
                        Memory.AddMessage(new ChatMessage("tool", result)
                        {
                            ToolId = call.Id,
                            ToolName = call.ToolName
                        });
                    }
                    else
                    {
                        Memory.AddMessage(new ChatMessage("tool", $"Error: Tool '{call.ToolName}' not found."));
                    }
                }

                // Recursive Re-evaluation: Ask the LLM to analyze the tool results
                await RunNextStepAsync();
            }
        }


        /// <summary>
        /// Sends current history to the LLM Provider for the next logical 'tick'.
        /// </summary>
        protected async Task RunNextStepAsync()
        {

            try
            {
                var response = await Provider.GenerateResponseAsync(Memory.History, _tools);
                await ProcessResponseAsync(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL] {Config.Name} execution error: {ex.Message}");
            }
        }
    }
}