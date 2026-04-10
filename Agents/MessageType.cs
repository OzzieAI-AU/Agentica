namespace OzzieAI.Agentica.Agents
{
    /// <summary>
    /// Defines all supported message types in the Agentica swarm.
    /// Used by AgentBus for routing and by agents for processing logic.
    /// </summary>
    public enum MessageType
    {
        /// <summary>
        /// High-level or delegated task sent to a worker or manager.
        /// </summary>
        TaskAssignment,

        /// <summary>
        /// Result returned after a worker or manager completes a task.
        /// </summary>
        TaskResult,

        /// <summary>
        /// Request for a high-level decision from the Boss.
        /// </summary>
        DecisionRequest,

        /// <summary>
        /// Response/decision sent back from the Boss.
        /// </summary>
        DecisionResponse,

        /// <summary>
        /// Knowledge or skill being transferred upward in the hierarchy.
        /// </summary>
        KnowledgeTransfer,

        /// <summary>
        /// Heartbeat or status update (for observability).
        /// </summary>
        StatusUpdate
    }
}