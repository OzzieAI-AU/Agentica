namespace OzzieAI.Agentica.Agents
{
    /// <summary>
    /// Defines the hierarchical roles an agent can assume within the OzzieAI Agentica system.
    /// </summary>
    public enum AgentRole
    {
        /// <summary>
        /// A basic execution agent responsible for performing concrete tasks.
        /// </summary>
        Worker,

        /// <summary>
        /// A supervisory agent that coordinates and manages one or more Worker agents.
        /// </summary>
        Manager,

        /// <summary>
        /// The top-level authoritative agent responsible for high-level decision making,
        /// strategy, and oversight of Managers and Workers.
        /// </summary>
        Boss
    }

    /// <summary>
    /// Represents the complete configuration required to spawn and initialize any agent
    /// in the OzzieAI Agentica framework.
    /// </summary>
    /// <remarks>
    /// This class serves as the blueprint for creating agents with specific identities,
    /// capabilities, and behavioral parameters. It encapsulates all necessary information
    /// for the agent lifecycle, including communication with LLMs and available tools.
    /// </remarks>
    public class AgentConfig
    {
        /// <summary>
        /// Gets the unique identifier for this agent configuration.
        /// </summary>
        /// <remarks>
        /// Automatically generated as a GUID upon object creation. This ID remains
        /// constant throughout the lifetime of the configuration and is used for
        /// tracking, logging, and distinguishing between agents.
        /// </remarks>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Gets or sets the human-readable name of the agent.
        /// </summary>
        /// <value>
        /// A friendly name used for display, logging, and identification purposes.
        /// </value>
        /// <example>"ResearchSpecialist", "CodeReviewer", "ProjectCoordinator"</example>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the role this agent will fulfill within the agent hierarchy.
        /// </summary>
        /// <value>
        /// The <see cref="AgentRole"/> that determines the agent's authority level
        /// and responsibilities.
        /// </value>
        public AgentRole Role { get; set; }

        /// <summary>
        /// The ID of this agent's direct boss (set automatically by Boss.StartManager / StartWorker).
        /// Never hard-code "BOSS_ID" again!
        /// </summary>
        public string? BossId 
        {
            get
            {
                return BossAgent.BossId;
            }
        }

        /// <summary>
        /// The ID of this agent's direct boss (set automatically by Boss.StartManager / StartWorker).
        /// Never hard-code "BOSS_ID" again!
        /// </summary>
        public string? ParentId { get; set; }

        /// <summary>
        /// Gets or sets a detailed description of the agent's primary task or purpose.
        /// </summary>
        /// <value>
        /// A natural language description that guides the agent's behavior and helps
        /// the LLM understand its mission and success criteria.
        /// </value>
        /// <example>"Analyze market trends and produce weekly summary reports"</example>
        public string TaskDescription { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the collection of tools available to this agent.
        /// </summary>
        /// <remarks>
        /// Tools are represented as a dictionary where the key is the tool/action name
        /// and the value is a description or endpoint reference. These tools are
        /// typically exposed to the LLM to enable function calling capabilities.
        /// </remarks>
        /// <example>
        /// {
        ///     { "web_search", "Search the internet for current information" },
        ///     { "read_file", "Read content from a local file path" }
        /// }
        /// </example>
        public Dictionary<string, string> Tools { get; set; } = new();

        /// <summary>
        /// Gets or sets the endpoint URL for the Language Model service this agent will use.
        /// </summary>
        /// <value>
        /// The base URL of the LLM API (e.g., OpenAI, Anthropic, or a local Ollama instance).
        /// </value>
        public ILlmProvider Provider { get; set; }
    }
}