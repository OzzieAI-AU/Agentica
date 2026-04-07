namespace OzzieAI.Agentica.Tools
{
    using System.Threading.Tasks;

    public interface IAgentTool
    {
        /// <summary>
        /// The unique name of the tool (e.g., "file_manager").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// A clear description for the LLM to understand when to use this tool.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Returns the JSON Schema that Ollama/Gemini/Grok require to call the tool.
        /// </summary>
        object GetToolDefinition();

        /// <summary>
        /// The actual logic execution. Takes JSON arguments from the LLM and returns a result string.
        /// </summary>
        Task<string> ExecuteAsync(string jsonArguments);
    }
}