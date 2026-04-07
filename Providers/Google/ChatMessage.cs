using System;
using System.Collections.Generic;
using System.Text;

namespace OzzieAI.Agentica.Providers.Google
{
    public class ChatMessage : IChatMessage
    {

        /// <summary>
        /// The identity of the message author. 
        /// Common values include "system", "user", "assistant", and "tool".
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// The primary text content of the message. 
        /// For 'tool' roles, this typically contains the stringified result of the execution.
        /// </summary>
        public object Content { get; set; }

        /// <summary>
        /// Base64 encoded binary data for multimodal processing. 
        /// Used for injecting images, audio, or video frames into the conversation context.
        /// </summary>
        public string? MediaData { get; set; }

        /// <summary>
        /// The IANA standard Media Type (e.g., "image/jpeg", "video/mp4") 
        /// describing the format of the <see cref="MediaData"/>.
        /// </summary>
        public string? MimeType { get; set; }

        /// <summary>
        /// The unique identifier for a specific tool execution request. 
        /// Essential for Grok and OpenAI providers to link function results back to the original call.
        /// </summary>
        public string? ToolId { get; set; }

        /// <summary>
        /// The human-readable name of the tool associated with this message.
        /// </summary>
        public string? ToolName { get; set; }


        public ChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }
}