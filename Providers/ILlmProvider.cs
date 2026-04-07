namespace OzzieAI.Agentica
{
    using OzzieAI.Agentica.Providers;
    using OzzieAI.Agentica.Tools;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    public interface ILlmProvider
    {
        /// <summary>
        /// Sends a conversation history and a list of available tools to the LLM.
        /// </summary>
        Task<IChatResponse> GenerateResponseAsync(List<IChatMessage> history, List<IAgentTool>? tools = null);

        /// <summary>
        /// Diagnostics check for the provider (Heartbeat).
        /// </summary>
        Task<bool> IsHealthyAsync();
    }
}