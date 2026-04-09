namespace OzzieAI.Agentica
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading.Channels;
    using System.Threading.Tasks;

    /// <summary>
    /// Facilitates high-throughput, asynchronous message passing between Agents.
    /// Acts as a central post office where each Agent possesses a private, non-blocking mailbox.
    /// </summary>
    public class AgentBus : IAgentBus
    {
        /// <summary>
        /// A thread-safe dictionary mapping unique Agent IDs to their specific communication Channel.
        /// This ensures that message routing is O(1) and safe across multiple CPU cores.
        /// </summary>
        private readonly ConcurrentDictionary<string, Channel<AgentMessage>> _mailboxes = new();

        /// <summary>
        /// Initializes a private "Mailbox" for a new Agent. 
        /// Uses an Unbounded channel to prevent backpressure, ensuring the sender can 
        /// fire-and-forget messages without waiting for the receiver to process them.
        /// </summary>
        /// <param name="agentId">The unique identifier for the Agent joining the network.</param>
        public void RegisterAgent(string agentId)
        {
            _mailboxes.TryAdd(agentId, Channel.CreateUnbounded<AgentMessage>(new UnboundedChannelOptions
            {
                SingleReader = true, // Optimizes performance as each agent reads its own mail
                SingleWriter = false // Multiple agents/sources can send to this mailbox
            }));
        }

        /// <summary>
        /// Provides the consumption end of the channel for a specific Agent.
        /// Agents use this reader to listen for incoming tasks or responses in a loop.
        /// </summary>
        /// <param name="agentId">The ID of the Agent requesting its inbox.</param>
        /// <returns>A ChannelReader capable of streaming AgentMessage objects.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if the Agent attempted to listen before registering.</exception>
        public ChannelReader<AgentMessage> GetReader(string agentId)
        {
            if (_mailboxes.TryGetValue(agentId, out var channel))
                return channel.Reader;

            throw new KeyNotFoundException($"Agent {agentId} is not registered on the bus.");
        }

        /// <summary>
        /// Dispatches a message to the intended recipient's mailbox.
        /// This is an awaitable operation that yields control back to the caller immediately 
        /// once the message is enqueued, maximizing system concurrency.
        /// </summary>
        /// <param name="message">The envelope containing metadata, payload, and routing info.</param>
        /// <returns>A ValueTask representing the asynchronous write operation.</returns>
        public async ValueTask SendAsync(AgentMessage message)
        {
            if (_mailboxes.TryGetValue(message.ReceiverId, out var channel))
            {
                // WriteAsync is used to ensure thread-safety and handle potential capacity limits
                await channel.Writer.WriteAsync(message);
            }
            else
            {
                // Log or handle dead-lettering if the recipient does not exist
                ConsoleLogger.WriteLine($"[BUS WARNING] Failed to deliver message to unknown Agent: {message.ReceiverId}", ConsoleColor.Red);
            }
        }
    }
}