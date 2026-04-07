namespace OzzieAI.Agentica
{
    using System;

    public enum MessageType
    {
        TaskAssignment,
        TaskResult,
        DecisionRequest,
        DecisionResponse,
        KnowledgeTransfer
    }

    /// <summary>
    /// The immutable message payload sent between agents over the Channels bus.
    /// </summary>
    public record AgentMessage(
        string SenderId,
        string ReceiverId,
        MessageType Type,
        string Content,
        DateTime Timestamp);
}