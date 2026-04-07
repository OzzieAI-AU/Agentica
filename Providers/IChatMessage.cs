namespace OzzieAI.Agentica.Providers
{
    using System.Collections.Generic;

    /// <summary>
    /// Defines the universal contract for messages within the Agentica ecosystem.
    /// This interface allows the framework to process diverse message types—including 
    /// human input, AI reasoning, tool results, and multimodal data—across different LLM providers.
    /// </summary>
    public interface IChatMessage
    {
        /// <summary>
        /// The identity of the message author. 
        /// Common values include "system", "user", "assistant", and "tool".
        /// </summary>
        string Role { get; set; }

        /// <summary>
        /// The primary text content of the message. 
        /// For 'tool' roles, this typically contains the stringified result of the execution.
        /// </summary>
        object Content { get; set; }

        /// <summary>
        /// Base64 encoded binary data for multimodal processing. 
        /// Used for injecting images, audio, or video frames into the conversation context.
        /// </summary>
        string? MediaData { get; set; }

        /// <summary>
        /// The IANA standard Media Type (e.g., "image/jpeg", "video/mp4") 
        /// describing the format of the <see cref="MediaData"/>.
        /// </summary>
        string? MimeType { get; set; }

        /// <summary>
        /// The unique identifier for a specific tool execution request. 
        /// Essential for Grok and OpenAI providers to link function results back to the original call.
        /// </summary>
        string? ToolId { get; set; }

        /// <summary>
        /// The human-readable name of the tool associated with this message.
        /// </summary>
        string? ToolName { get; set; }
    }
}