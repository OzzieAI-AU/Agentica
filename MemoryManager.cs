using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OzzieAI.Agentica.Providers;
using OzzieAI.Agentica.Providers.Ollama;

namespace OzzieAI.Agentica
{
    /// <summary>
    /// Manages the temporal continuity of an agent by performing "Context Conciliation."
    /// This service prevents token-window overflow by compressing middle-history into 
    /// semantic summaries while preserving critical mission anchors and recent tactical context.
    /// </summary>
    public class MemoryManager
    {
        /// <summary>
        /// Compresses the agent's chat history when it exceeds operational limits.
        /// It preserves the initial system prompt (The Anchor) and the most recent 
        /// interaction buffer, while summarizing everything in between.
        /// </summary>
        /// <param name="history">The full chronological list of chat messages.</param>
        /// <param name="summarizer">The LLM client designated for compression tasks (typically a fast, local model).</param>
        /// <returns>A condensed list of messages that fits within the model's context window.</returns>
        public async Task<List<ChatMessage>> ConciliateMemoryAsync(List<ChatMessage> history, OllamaLlmClient summarizer)
        {
            // Threshold Check: Only compress if we have enough depth to justify a summary.
            // 8 messages allow for 1 System Anchor, 4 Middle, and 3 Recent.
            if (history.Count <= 8) return history;

            // 1. THE ANCHOR: Always preserve the very first message (usually the System Prompt/Mission).
            var firstPrompt = history[0];

            // 2. THE BUFFER: Preserve the last 3 messages to maintain the current "train of thought."
            // This usually includes the Assistant's last thought and the resulting Tool output.
            var recentContext = history.TakeLast(3).ToList();

            // 3. THE MIDDLE GROUND: The content to be compressed.
            var middleGround = history.Skip(1).Take(history.Count - 4).ToList();

            // Build a structured string of the middle ground for the summarizer.
            var sb = new StringBuilder();
            foreach (var m in middleGround)
            {
                // We ignore previous summaries to prevent "Inception-style" loss of detail.
                if (m.Content.ToString().StartsWith("Summary of previous progress:")) continue;
                sb.AppendLine($"{m.Role.ToUpper()}: {m.Content}");
            }

            // Construct the Compression Instruction
            string summaryInstructions =
                "Summarize the following technical execution steps into a single concise paragraph. " +
                "CRITICAL: Retain all specific file paths, error codes, method names, and final decisions. " +
                "Do not lose technical data during compression.";

            var compressionPayload = new List<IChatMessage>
            {
                new ChatMessage { Role = "system", Content = "You are a high-fidelity memory compression utility for an autonomous agent." },
                new ChatMessage { Role = "user", Content = $"{summaryInstructions}\n\nCONTENT TO COMPRESS:\n{sb}" }
            };

            try
            {
                // Execute the summary pass. Using 'null' for tools as the summarizer shouldn't take actions.
                var response = await summarizer.GenerateResponseAsync(compressionPayload, null);

                // Ensure the response content is extracted safely.
                string condensedHistory = response?.Content?.ToString()?.Trim() ?? "Manual compression failed.";

                // Visual feedback for the operator
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n[MEMORY]: Conciliated {middleGround.Count} messages into a single summary block.");
                Console.ResetColor();

                // Reconstruct the memory stream
                return new List<ChatMessage>
                {
                    firstPrompt,
                    new ChatMessage
                    {
                        Role = "system",
                        Content = $"Summary of previous progress: {condensedHistory}"
                    },
                    recentContext[0],
                    recentContext[1],
                    recentContext[2]
                };
            }
            catch (Exception ex)
            {
                // Safety Fallback: If summarization fails, return the original history to avoid agent amnesia.
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[MEMORY ERROR]: Conciliation failed: {ex.Message}");
                Console.ResetColor();
                return history;
            }
        }
    }
}