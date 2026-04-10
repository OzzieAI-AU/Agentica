namespace OzzieAI.Agentica.Agents
{
    using System;

    /// <summary>
    /// The immutable message payload sent between agents over the Channels bus.
    /// </summary>
    public record AgentMessage(
    string SenderId,
    string ReceiverId,
    MessageType Type,
    string Content,
    DateTime Timestamp,
    string? Id = null);   // ← Add this
}