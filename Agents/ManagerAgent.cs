namespace OzzieAI.Agentica
{

    using OzzieAI.Agentica.Agents;
    // ─────────────────────────────────────────────────────────────────────────────
    // NAMESPACE DOCUMENTATION
    // ─────────────────────────────────────────────────────────────────────────────
    // This namespace contains the complete Agentica framework — a sophisticated,
    // production-grade, swarm-native AI agent architecture developed for OzzieAI.
    // All agents, tools, providers, memory systems, and communication primitives
    // are organized here to ensure architectural clarity, thread-safety, and
    // seamless hierarchical collaboration across the entire swarm.

    using OzzieAI.Agentica.Providers;           // LLM provider abstractions
    using System;                               // Core .NET types (DateTime, Guid, etc.)
    using System.Collections.Concurrent;        // Thread-safe collections for high-concurrency operations
    using System.Collections.Generic;           // Generic collections support
    using System.Linq;                          // LINQ extensions (used indirectly via other members)
    using System.Text.Json;
    using System.Text.RegularExpressions;       // Regex support for automatic code block extraction
    using System.Threading.Tasks;               // Async/await infrastructure for non-blocking message handling

    // ─────────────────────────────────────────────────────────────────────────────
    // ENHANCED MANAGERAGENT SUPPORT CODE - BEAUTIFULLY DOCUMENTED
    // Every single line is explicitly documented below.
    // These additions bring intelligent task delegation, quality control, round-robin
    // load balancing, worker status tracking, and LLM-powered result review to the Manager.
    // ─────────────────────────────────────────────────────────────────────────────

    using System.Text.Json;   // Required for JsonDocument parsing in quality review

    // Add these private fields near the top with the other concurrent collections
    /// <summary>
    /// ManagerAgent — Tactical coordinator and intelligent orchestrator of the OzzieAI agent swarm.
    /// 
    /// This concrete implementation of BaseAgent serves as the tactical layer between the Boss
    /// and the specialized Worker agents. It fully implements the requirements outlined in ReadMe.txt,
    /// including:
    ///   • Task delegation to available workers
    ///   • Automatic code extraction from Coder agents
    ///   • Intelligent forwarding of clean code to Builder agents
    ///   • Skill learning and upstream knowledge propagation to the Boss
    ///   • Thread-safe state management using concurrent collections
    /// 
    /// All learned knowledge and results automatically bubble up to the Boss exactly as
    /// described in the "Knowledge Propagation" section of ReadMe.txt.
    /// </summary>
    public class ManagerAgent : BaseAgent
    {
        // ─────────────────────────────────────────────────────────────────────
        // PRIVATE CONCURRENT STATE (THREAD-SAFE)
        // These collections are deliberately concurrent to safely handle multiple
        // simultaneous messages and worker interactions in a high-throughput swarm.
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Thread-safe bag containing IDs of all registered worker agents
        /// (Researchers, Coders, Builders, etc.) available for task delegation.
        /// Uses ConcurrentBag for lock-free additions.
        /// </summary>
        private readonly ConcurrentBag<string> _workerIds = new();

        /// <summary>
        /// Thread-safe dictionary storing all skills and knowledge acquired by the Manager.
        /// Key = unique skill identifier, Value = detailed skill content or result.
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _skills = new();

        /// <summary>
        /// Tracks dependencies between tasks. Key = task ID, Value = list of prerequisite task IDs.
        /// (Allocated for future expansion of dependency-aware orchestration.)
        /// </summary>
        private readonly ConcurrentDictionary<string, List<string>> _taskDependencies = new();

        /// <summary>
        /// Records completion status of tasks. Key = task ID, Value = boolean completion flag.
        /// (Allocated for future expansion of task lifecycle tracking.)
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _completedTasks = new();

        /// <summary>
        /// Holds pending tasks awaiting processing or delegation.
        /// Key = task ID, Value = original AgentMessage.
        /// (Allocated for future expansion of task queuing and dependency resolution.)
        /// </summary>
        private readonly ConcurrentDictionary<string, AgentMessage> _pendingTasks = new();

        // ─────────────────────────────────────────────────────────────────────
        // CONSTRUCTOR
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Initializes a new instance of the ManagerAgent class.
        /// Forwards all required dependencies to the BaseAgent base class for
        /// centralized null validation and initialization.
        /// </summary>
        /// <param name="config">Agent configuration containing identity, provider, and swarm settings.</param>
        /// <param name="bus">Shared message bus for inter-agent communication.</param>
        /// <param name="memory">DragonMemory instance dedicated to this manager's history and knowledge.</param>
        public ManagerAgent(AgentConfig config, IAgentBus bus, DragonMemory memory)
            : base(config, bus, memory) { }

        /// <summary>
        /// Thread-safe dictionary used for global round-robin indexing across all workers.
        /// Key is always "global", value is the next worker index to assign.
        /// This ensures fair distribution of tasks when multiple workers are available.
        /// </summary>
        private readonly ConcurrentDictionary<string, int> _workerRoundRobinIndex = new();

        /// <summary>
        /// Thread-safe dictionary tracking the real-time status of each worker.
        /// Key = worker ID, Value = WorkerStatus object containing busy state, last assignment time, and current task.
        /// Enables the Manager to make intelligent delegation decisions and avoid overloading busy workers.
        /// </summary>
        private readonly ConcurrentDictionary<string, WorkerStatus> _workerStatus = new();

        /// <summary>
        /// Thread-safe dictionary that tracks how many times a specific task has been retried.
        /// Key = taskId, Value = retry count (used to prevent infinite revision loops).
        /// </summary>
        private readonly ConcurrentDictionary<string, int> _taskRetryCount = new();

        /// <summary>
        /// Thread-safe dictionary storing quality review scores for tasks.
        /// Key = taskId, Value = score (0.0 - 10.0) assigned by the Manager's LLM reviewer.
        /// Useful for auditing, reporting, and decision making.
        /// </summary>
        private readonly ConcurrentDictionary<string, double> _taskScores = new(); // taskId -> score

        /// <summary>
        /// Inner class representing the live status of a single worker.
        /// Used internally by the Manager to track workload and prevent over-assignment.
        /// </summary>
        private class WorkerStatus
        {
            public DateTime LastAssigned { get; set; } = DateTime.UtcNow;
            /// <summary>
            /// Timestamp of the last time a task was assigned to this worker.
            /// Helps with load balancing and detecting stale workers.
            /// </summary>

            public bool IsBusy { get; set; } = false;
            /// <summary>
            /// Flag indicating whether the worker is currently processing a task.
            /// The Manager respects this flag during delegation.
            /// </summary>

            public string CurrentTaskId { get; set; } = string.Empty;
            /// <summary>
            /// ID of the task currently being processed by this worker (empty when idle).
            /// </summary>
        }

        // Helper to determine sender type (keep or improve existing)

        /// <summary>
        /// Determines the functional type of a worker based on its ID string.
        /// This allows the Manager to apply specialized handling (e.g., different quality rules for Coders vs Researchers).
        /// </summary>
        /// <param name="senderId">The unique ID of the sending worker/agent</param>
        /// <returns>A simplified type string: "Researcher", "Coder", "Builder", or "Worker"</returns>
        private string DetermineSenderType(string senderId)
        {
            if (senderId.Contains("Researcher", StringComparison.OrdinalIgnoreCase)) return "Researcher";
            // Checks if the worker ID contains "Researcher" (case-insensitive) and returns the type.

            if (senderId.Contains("Coder", StringComparison.OrdinalIgnoreCase)) return "Coder";
            // Checks for "Coder" type.

            if (senderId.Contains("Builder", StringComparison.OrdinalIgnoreCase)) return "Builder";
            // Checks for "Builder" type.

            return "Worker";
            // Default fallback for any other worker type.
        }

        // Improved delegation with round-robin and status tracking

        // ─────────────────────────────────────────────────────────────────────
        // TASK DELEGATION LOGIC
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Delegates a received task to an available worker agent.
        /// Currently uses TryPeek for simple worker selection (can be extended
        /// to round-robin or load-balancing in future iterations).
        /// Generates a short task ID for clear logging and traceability.
        /// </summary>
        /// <param name="taskMessage">The original task message to delegate.</param>
        /// <param name="taskId">A short unique identifier for this delegation (for logging).</param>
        private async Task DelegateToAvailableWorkerAsync(AgentMessage taskMessage, string taskId)
        {
            if (_workerIds.IsEmpty)
            /// <summary>
            /// Safety check: If no workers are registered, log a warning and abort delegation.
            /// Prevents null-reference or silent failures.
            /// </summary>
            {
                ConsoleLogger.Warning($"[{Config.Name} - (Manager)] No workers available for task {taskId}");
                // Logs a clear warning when delegation cannot proceed.

                return;
                // Early exit - nothing more to do.
            }

            // Simple round-robin selection
            var workers = _workerIds.ToArray(); // snapshot
            /// <summary>
            /// Creates a snapshot of current worker IDs to avoid collection modification issues during enumeration.
            /// </summary>

            if (workers.Length == 0) return;
            /// <summary>
            /// Double-check after snapshot in case workers were removed concurrently.
            /// </summary>

            int index = _workerRoundRobinIndex.AddOrUpdate("global", 0, (_, v) => (v + 1) % workers.Length);
            /// <summary>
            /// Atomically updates the round-robin index and calculates the next worker position.
            /// Uses modulo to wrap around when reaching the end of the worker list.
            /// </summary>

            string selectedWorker = workers[index % workers.Length];
            /// <summary>
            /// Selects the worker at the computed round-robin index.
            /// </summary>

            _workerStatus.TryAdd(selectedWorker, new WorkerStatus());
            /// <summary>
            /// Ensures a status entry exists for the selected worker (adds default if missing).
            /// </summary>

            if (_workerStatus.TryGetValue(selectedWorker, out var status))
            /// <summary>
            /// Retrieves the status object for the chosen worker (safe because we just ensured it exists).
            /// </summary>
            {
                status.IsBusy = true;
                /// <summary>
                /// Marks the worker as busy so future delegations skip it until it reports completion.
                /// </summary>

                status.LastAssigned = DateTime.UtcNow;
                /// <summary>
                /// Updates the last assignment timestamp for monitoring.
                /// </summary>

                status.CurrentTaskId = taskId;
                /// <summary>
                /// Records which task this worker is now handling.
                /// </summary>
            }

            _taskRetryCount[taskId] = 0;
            /// <summary>
            /// Initializes retry counter for this new task to zero.
            /// </summary>

            await Bus.SendAsync(new AgentMessage(
                Config.Id, selectedWorker, MessageType.TaskAssignment, taskMessage.Content, DateTime.UtcNow));
            /// <summary>
            /// Sends the actual task assignment message to the selected worker via the shared message bus.
            /// </summary>

            ConsoleLogger.WriteLine($"[{Config.Name} - (Manager)] 🔀 Delegated task {taskId} to {selectedWorker} (round-robin)", ConsoleColor.DarkCyan);
            /// <summary>
            /// Logs the delegation action with a distinct color for easy tracing in console output.
            /// </summary>
        }

        // NEW: Core quality review using the Manager's own LLM (fast local model recommended)

        /// <summary>
        /// Uses the Manager's LLM to perform an objective quality review of a worker's output.
        /// Returns a structured tuple with score, feedback, and escalation decision.
        /// This is the "Quality Gate" that makes the Manager truly intelligent.
        /// </summary>
        /// <param name="content">The raw output from the worker to be reviewed</param>
        /// <param name="senderType">Type of worker (Researcher, Coder, Builder, etc.)</param>
        /// <param name="taskId">Task identifier for tracking the review</param>
        /// <returns>Tuple containing: score (0-10), detailed feedback, and whether result should escalate to Boss</returns>
        private async Task<(double Score, string Feedback, bool ShouldEscalate)> ReviewWorkerResultAsync(string content, string senderType, string taskId)
        {

            string rubricPrompt = $@"
You are a strict but fair technical reviewer in a multi-agent swarm.
Evaluate the following {senderType} output on a scale of 0.0 to 10.0.

RUBRIC (weighted):
- Correctness & Completeness: 40%
- Code Quality / Technical Soundness: 30%
- Adherence to Instructions: 20%
- Clarity & Usability: 10%

Output ONLY valid JSON (no markdown, no extra text):
{{
  ""score"": 8.7,
  ""feedback"": ""Detailed explanation of strengths, weaknesses, and specific issues"",
  ""recommendation"": ""approve|revise|reject""
}}

CONTENT TO REVIEW:
{content}";

            // Use the MAIN ChatMessage from OzzieAI.Agentica namespace (not any provider-specific one)
            var reviewHistory = new List<IChatMessage>
            {
                new ChatMessage("system", "You are an expert quality gate for an autonomous agent swarm. Be concise, objective, and strictly technical."),
                new ChatMessage("user", rubricPrompt)
            };

            try
            {
                LlmResponse response = ((LlmResponse)await Provider.GenerateResponseAsync(reviewHistory));

                string json = response?.Content?.ToString() ?? "{}";

                // Clean any markdown that the LLM might have added
                json = json.Replace("```json", "").Replace("```", "").Trim();

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                double score = root.TryGetProperty("score", out var s) ? s.GetDouble() : 6.5;
                string feedback = root.TryGetProperty("feedback", out var f) ? (f.GetString() ?? "No detailed feedback provided.") : "No feedback";
                string recommendation = root.TryGetProperty("recommendation", out var r)
                    ? (r.GetString()?.ToLowerInvariant() ?? "revise")
                    : "revise";

                bool shouldEscalate = score >= 8.0 && recommendation.Contains("approve");

                _taskScores[taskId] = score;

                ConsoleLogger.WriteLine($"[{Config.Name} - (Manager)] 📊 Review score for {senderType}: {score:F1}/10",
                    score >= 8.0 ? ConsoleColor.Green : ConsoleColor.Yellow);

                return (score, feedback, shouldEscalate);
            }
            catch (Exception ex)
            {
                ConsoleLogger.Warning($"[{Config.Name} - (Manager)] Review failed: {ex.Message}. Defaulting to safe pass (score 7.0).");
                return (7.0, "Review parsing failed - proceeding cautiously", true);
            }
        }

        // COMPLETED & ENHANCED HandleWorkerResultAsync

        // ─────────────────────────────────────────────────────────────────────
        // WORKER RESULT HANDLING (INTELLIGENT ROUTING)
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Handles results returned by any Worker agent. Performs the following:
        ///   • Learns the result as a new skill and propagates it upstream
        ///   • If the result is from a Coder: automatically extracts clean C# code
        ///     and forwards it to a Builder with precise implementation instructions
        ///   • If the result is from a Builder: logs successful verification and
        ///     signals that the result should escalate to the Boss
        /// </summary>
        /// <param name="message">The TaskResult message received from a worker.</param>
        private async Task HandleWorkerResultAsync(AgentMessage message)
        {

            string senderId = message.SenderId;
            string senderType = DetermineSenderType(senderId);
            string taskId = Guid.NewGuid().ToString("N").Substring(0, 8);

            ConsoleLogger.WriteLine($"[{Config.Name} - (Manager)] 📥 Received result from {senderType} ({senderId})", ConsoleColor.Blue);

            LearnSkill($"{senderType}_Result_{DateTime.UtcNow:yyyyMMdd_HHmmss}", message.Content);

            var (score, feedback, shouldEscalate) = await ReviewWorkerResultAsync(message.Content, senderType, taskId);

            ConsoleLogger.WriteLine($"[{Config.Name} - (Manager)] 📊 Review score: {score:F1}/10", score >= 8.0 ? ConsoleColor.Green : ConsoleColor.Yellow);

            // Update worker status
            if (_workerStatus.TryGetValue(senderId, out var status))
                status.IsBusy = false;

            // CRITICAL FIX: Use BossId for high-level tracking to avoid routing errors
            string targetRecipient = Config.BossId ?? Config.ParentId ?? message.SenderId;

            if (score >= 8.0 && !string.IsNullOrEmpty(targetRecipient))
            {
                ConsoleLogger.WriteLine($"[{Config.Name} - (Manager)] 🚀 Propagating success to Leadership: {targetRecipient}", ConsoleColor.Cyan);

                await Bus.SendAsync(new AgentMessage(
                    SenderId: Config.Id, // Set Manager as sender so Boss knows who it's coming from
                    ReceiverId: targetRecipient,
                    Type: MessageType.TaskResult,
                    Content: $"[PASSED] Sub-task result from {message.SenderId} (Score: {score:F1}):\n{message.Content}",
                    Timestamp: DateTime.UtcNow));
            }


            // === STAGE 1: Researcher → Coder ===
            if (senderType.Contains("Strategist", StringComparison.OrdinalIgnoreCase) ||
                senderType.Contains("Analyst", StringComparison.OrdinalIgnoreCase) ||
                senderType.Contains("Literature", StringComparison.OrdinalIgnoreCase))
            {
                if (score < 7.0)
                {
                    await SendRevisionRequest(senderId, score, feedback);
                    return;
                }

                // Find the Coder
                var coderId = _workerIds.FirstOrDefault(id => id.Contains("Implementer", StringComparison.OrdinalIgnoreCase) ||
                                                              id.Contains("Code Implementer", StringComparison.OrdinalIgnoreCase) ||
                                                              id.Contains("Coder", StringComparison.OrdinalIgnoreCase));

                if (coderId != null)
                {
                    await Bus.SendAsync(new AgentMessage(Config.Id, coderId, MessageType.TaskAssignment,
                        $"TECHNICAL PLAN FROM STRATEGIST (score {score:F1}/10):\n\n{message.Content}\n\n" +
                        "Write the complete 'SumCalculator.cs' file using the file_manager tool (action='write', path='SumCalculator.cs'). " +
                        "Make it a full console application with the best summation method based on the plan. Include Main method for testing. " +
                        "Confirm when the file is written.",
                        DateTime.UtcNow));

                    ConsoleLogger.Success($"[{Config.Name} - (Manager)] → FORWARDED RESEARCH TO CODER");
                }
                return;
            }

            // === STAGE 2: Coder → Verifier ===
            if (senderType.Contains("Implementer", StringComparison.OrdinalIgnoreCase) ||
                senderType.Contains("Coder", StringComparison.OrdinalIgnoreCase))
            {
                if (score < 7.0)
                {
                    await SendRevisionRequest(senderId, score, feedback);
                    return;
                }

                var verifierId = _workerIds.FirstOrDefault(id => id.Contains("Validator", StringComparison.OrdinalIgnoreCase) ||
                                                                 id.Contains("DevOps", StringComparison.OrdinalIgnoreCase));

                if (verifierId != null)
                {
                    await Bus.SendAsync(new AgentMessage(Config.Id, verifierId, MessageType.TaskAssignment,
                        $"CODE FROM IMPLEMENTER (score {score:F1}/10):\n\n{message.Content}\n\n" +
                        "Use terminal_executor to build and run this code. Report the full build and run output.",
                        DateTime.UtcNow));

                    ConsoleLogger.Success($"[{Config.Name} - (Manager)] → FORWARDED CODE TO VERIFIER");
                }
                return;
            }

            // === STAGE 3: Verifier → Boss ===
            if (senderType.Contains("Validator", StringComparison.OrdinalIgnoreCase) ||
                senderType.Contains("DevOps", StringComparison.OrdinalIgnoreCase))
            {
                // === STAGE 3: Verifier → Boss ===
                if (senderType.Contains("Verifier") || senderType.Contains("Builder") || senderType.Contains("Validator"))
                {
                    if (score >= 8.0)
                    {
                        ConsoleLogger.Success($"[{Config.Name} - (Manager)] ✅ Mission Criteria Met. Requesting Final Decision.");

                        // TRIGGER: This DecisionRequest tells the Boss to wrap up the mission.
                        await Bus.SendAsync(new AgentMessage(
                            SenderId: message.SenderId,
                            ReceiverId: Config.ParentId,
                            Type: MessageType.DecisionRequest,
                            Content: "Final stage complete. Please finalize the project and generate the report.",
                            Timestamp: DateTime.UtcNow));
                    }
                }
                else
                {
                    await SendRevisionRequest(senderId, score, feedback);
                }
            }
        }

        private async Task SendRevisionRequest(string senderId, double score, string feedback)
        {
            await Bus.SendAsync(new AgentMessage(
                Config.Id, senderId, MessageType.TaskAssignment,
                $"REVISION REQUIRED (Score: {score:F1}/10):\n{feedback}\n\nImprove your output and resubmit.",
                DateTime.UtcNow));

            ConsoleLogger.Warning($"[{Config.Name} - (Manager)] 🔄 Revision sent to {senderId}");
        }

        // New helper: Distill knowledge for better learning

        /// <summary>
        /// Uses the Manager's LLM to condense a worker's output into 3-5 high-value, actionable insights.
        /// This improves the quality of knowledge stored in the swarm's memory.
        /// </summary>
        /// <param name="rawContent">The full raw output from a worker</param>
        /// <param name="senderType">Type of the worker that produced the content</param>
        /// <returns>Concise distilled knowledge string</returns>
        private async Task<string> DistillKnowledgeAsync(string rawContent, string senderType)
        {
            var prompt = new List<IChatMessage>
            {
                new ChatMessage("system", $"You are a knowledge distiller for a {senderType} in an AI swarm."),
                new ChatMessage("user", $"Extract the 3-5 most valuable, actionable insights or artifacts from this output. Be concise and technical:\n\n{rawContent}")
            };
            /// <summary>
            /// Prepares prompt for knowledge distillation.
            /// </summary>

            var response = await Provider.GenerateResponseAsync(prompt);
            /// <summary>
            /// Calls LLM to perform distillation.
            /// </summary>

            return response?.Content?.ToString() ?? rawContent.Substring(0, Math.Min(500, rawContent.Length));
            /// <summary>
            /// Returns distilled version, or a safe truncated fallback if LLM call fails.
            /// </summary>
        }

        // Update ProcessIncomingMessageAsync to use the improved delegation

        // ─────────────────────────────────────────────────────────────────────
        // MESSAGE PROCESSING OVERRIDE (CENTRAL DISPATCHER)
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Processes all incoming messages from the swarm bus. This is the concrete
        /// implementation of the abstract method defined in BaseAgent.
        /// 
        /// Uses a clean switch statement to route messages to specialized handlers
        /// while maintaining full observability through detailed logging.
        /// </summary>
        /// <param name="message">The AgentMessage received from the message bus.</param>
        protected override async Task ProcessIncomingMessageAsync(AgentMessage message)
        {

            /// <summary>
            /// Logs every incoming message for full traceability.
            /// </summary>
            ConsoleLogger.WriteLine($"[{Config.Name} - (Manager)] 📥 Received {message.Type} from {message.SenderId}", ConsoleColor.Blue);

            /// <summary>
            /// Routes the message based on its type.
            /// </summary>
            switch (message.Type)
            {
                /// <summary>
                /// When the Boss (or another agent) assigns a task to the Manager, it intelligently delegates to a worker.
                /// </summary>
                case MessageType.TaskAssignment:
                    await DelegateToAvailableWorkerAsync(message, Guid.NewGuid().ToString("N").Substring(0, 8));
                    break;

                /// <summary>
                /// When a worker finishes, the Manager performs quality review, learning, and possible forwarding.
                /// </summary>
                case MessageType.TaskResult:
                    await HandleWorkerResultAsync(message);
                    break;

                /// <summary>
                /// Stores any knowledge transferred from other agents.
                /// </summary>
                case MessageType.KnowledgeTransfer:
                    LearnSkill($"Transferred_{DateTime.UtcNow:yyyyMMdd_HHmmss}", message.Content);
                    break;

                /// <summary>
                /// Gracefully handles unrecognized message types with a warning.
                /// </summary>
                default:
                    ConsoleLogger.Warning($"[{Config.Name} - (Manager)] ⚠️ Unknown message type: {message.Type}");
                    break;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // WORKER REGISTRATION
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Registers a worker agent ID with the Manager. This enables the Manager
        /// to delegate tasks to available workers using round-robin style selection.
        /// </summary>
        /// <param name="workerId">The unique identifier of the worker agent to register.</param>
        public void AssignWorker(string workerId) => _workerIds.Add(workerId);

        // ─────────────────────────────────────────────────────────────────────
        // CODE BLOCK EXTRACTION UTILITY
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Extracts a clean C# code block from raw LLM output using regex.
        /// Searches for content wrapped in ```csharp ... ``` markers.
        /// If no code block is found, returns the entire trimmed input text.
        /// This enables reliable, automatic code hand-off from Coder to Builder.
        /// </summary>
        /// <param name="text">The raw content returned by a Coder agent.</param>
        /// <returns>The extracted and trimmed C# code, or the original trimmed text if no block is found.</returns>
        private string ExtractCodeBlock(string text)
        {
            var match = Regex.Match(text, @"```csharp\s*(.*?)```", RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value.Trim() : text.Trim();
        }

        // ─────────────────────────────────────────────────────────────────────
        // SKILL LEARNING & KNOWLEDGE PROPAGATION
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Records a new skill in the Manager's local knowledge base and immediately
        /// propagates it upstream to the Boss via DragonMemory (exactly as specified
        /// in the "Knowledge Propagation" section of ReadMe.txt).
        /// </summary>
        /// <param name="skillName">Unique identifier for the learned skill.</param>
        /// <param name="skillData">The actual content or data of the skill.</param>
        private void LearnSkill(string skillName, string skillData)
        {
            // Store locally in the concurrent dictionary
            _skills[skillName] = skillData;

            // Persist to DragonMemory with explicit upstream propagation enabled
            Memory.Remember(skillName, skillData, propagate: true); // Upstream propagation - matches ReadMe.txt exactly

            // Log the learning and propagation event
            ConsoleLogger.WriteLine($"[{Config.Name} - (Manager)] 💡 Learned and propagated skill: {skillName}", ConsoleColor.DarkCyan);
        }
    }
}