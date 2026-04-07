namespace OzzieAI.Agentica
{
    using System.Collections.Generic;
    using System.Threading.Channels;
    using System.Threading.Tasks;

    /// <summary>
    /// Defines the contract for the asynchronous communication backbone of the Agentica framework.
    /// Implementations are responsible for routing messages between autonomous agents 
    /// while maintaining thread safety and minimizing allocation overhead.
    /// </summary>
    public interface IAgentBus
    {
        /// <summary>
        /// Allocates and initializes a dedicated communication stream (Mailbox) for an agent.
        /// This must be called before an agent attempts to send or receive messages.
        /// </summary>
        /// <param name="agentId">The unique identifier of the agent to register.</param>
        void RegisterAgent(string agentId);

        /// <summary>
        /// Retrieves the consumption stream for a specific agent.
        /// This allows the agent to asynchronously listen for incoming messages without 
        /// polling or blocking the main execution thread.
        /// </summary>
        /// <param name="agentId">The unique identifier of the agent.</param>
        /// <returns>A <see cref="ChannelReader{T}"/> for streaming incoming <see cref="AgentMessage"/> objects.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if the agentId has not been registered.</exception>
        ChannelReader<AgentMessage> GetReader(string agentId);

        /// <summary>
        /// Dispatches a message to the recipient specified in the <paramref name="message"/> metadata.
        /// This operation is optimized for high concurrency using <see cref="ValueTask"/> 
        /// to reduce heap allocations during high-frequency messaging.
        /// </summary>
        /// <param name="message">The envelope containing the task, payload, and routing identifiers.</param>
        /// <returns>A <see cref="ValueTask"/> representing the asynchronous completion of the dispatch.</returns>
        ValueTask SendAsync(AgentMessage message);
    }
}