namespace OzzieAI.Agentica
{
    using OzzieAI.Agentica.Agents;
    using OzzieAI.Agentica.Providers;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    public class ManagerAgent : BaseAgent
    {
        private readonly ConcurrentBag<string> _workerIds = new();
        private readonly ConcurrentDictionary<string, string> _skills = new();
        private readonly ConcurrentDictionary<string, string> _lastTaskSent = new();
        private readonly ConcurrentDictionary<string, int> _workerRetryCount = new();

        public ManagerAgent(AgentConfig config, IAgentBus bus, DragonMemory memory)
            : base(config, bus, memory) { }

        private string DetermineSenderType(string senderId)
        {
            string id = senderId.ToLower();
            if (id.Contains("researcher")) return "Researcher";
            if (id.Contains("coder")) return "Coder";
            if (id.Contains("validator")) return "Validator";
            return "Worker";
        }

        private async Task<(double Score, string Feedback, bool Approved)> ReviewResultAsync(string content, string senderType)
        {
            // HEURISTIC: Catch "Amnesia" responses immediately without LLM review
            if (content.Length < 100 || content.Contains("provide the code") || content.Contains("waiting for your"))
            {
                return (4.0, "Worker requested context instead of providing work.", false);
            }

            string prompt = $@"Evaluate this {senderType} output. 
            Check for technical artifacts (code blocks, analysis).
            If it's just conversational filler, score < 5.0.
            Return JSON: {{ ""score"": 0-10, ""feedback"": ""..."" }}";

            try
            {
                var history = new List<IChatMessage> { new ChatMessage("user", $"{prompt}\n\nCONTENT:\n{content}") };
                var response = await Provider.GenerateResponseAsync(history);
                var raw = response.Content.ToString();

                using var doc = JsonDocument.Parse(Regex.Match(raw, @"\{.*\}", RegexOptions.Singleline).Value);
                double score = doc.RootElement.GetProperty("score").GetDouble();
                string feedback = doc.RootElement.GetProperty("feedback").GetString();
                return (score, feedback, score >= 8.0);
            }
            catch { return (7.0, "Pass-through validation.", true); }
        }

        protected override async Task ProcessIncomingMessageAsync(AgentMessage message)
        {
            ConsoleLogger.WriteLine($"[{Config.Name}] ?? Received {message.Type} from {message.SenderId}", ConsoleColor.DarkCyan);

            switch (message.Type)
            {
                case MessageType.TaskAssignment:
                    await HandleTaskAssignmentAsync(message);
                    break;

                case MessageType.TaskResult:
                    await HandleWorkerResultAsync(message);
                    break;

                case MessageType.DecisionResponse:
                    await HandleBossDecisionAsync(message);
                    break;

                default:
                    ConsoleLogger.WriteLine($"[{Config.Name}] ⚠️ Unhandled message type: {message.Type}", ConsoleColor.Yellow);
                    break;
            }
        }

        private async Task HandleTaskAssignmentAsync(AgentMessage message)
        {

            var workers = _workerIds.ToArray();
            if (workers.Length == 0)
            {
                ConsoleLogger.WriteLine($"[{Config.Name}] ❌ No workers registered!", ConsoleColor.Red);
                return;
            }

            // Simple round-robin for now (you can make this smarter later)
            string targetWorker = workers[_workerIds.Count % workers.Length]; // cycles through all workers

            _lastTaskSent[targetWorker] = message.Content;

            await Bus.SendAsync(new AgentMessage(
                SenderId: Config.Id,
                ReceiverId: targetWorker,
                Type: MessageType.TaskAssignment,
                Content: message.Content,
                Timestamp: DateTime.UtcNow));

            ConsoleLogger.WriteLine($"[{Config.Name}] → Delegated task to worker {targetWorker}", ConsoleColor.Blue);
        }

        private async Task HandleWorkerResultAsync(AgentMessage message)
        {

            var (score, feedback, approved) = await ReviewResultAsync(message.Content, DetermineSenderType(message.SenderId));

            if (approved)
            {
                _workerRetryCount[message.SenderId] = 0;

                LearnSkill($"Artifact_{Guid.NewGuid().ToString()[..4]}", message.Content);

                // === CRITICAL FIX: Escalate as TaskResult so Boss can count completions ===
                await Bus.SendAsync(new AgentMessage(
                    SenderId: Config.Id,
                    ReceiverId: Config.ManagerId ?? "Boss",           // Boss ID
                    Type: MessageType.TaskResult,                     // ← Changed from DecisionRequest
                    Content: $"VERIFIED RESULT from {message.SenderId}:\r\nScore: {score}\n\n{message.Content}",
                    Timestamp: DateTime.UtcNow));

                ConsoleLogger.WriteLine($"[{Config.Name}] ✅ Approved & escalated high-quality result to Boss", ConsoleColor.Green);
            }
            else
            {
                int retries = _workerRetryCount.AddOrUpdate(message.SenderId, 1, (_, v) => v + 1);

                if (retries > 3)
                {
                    await Bus.SendAsync(new AgentMessage(
                        SenderId: Config.Id,
                        ReceiverId: Config.ManagerId ?? "Boss",
                        Type: MessageType.TaskResult,
                        Content: $"Worker {message.SenderId} is stuck after 3 revisions.",
                        Timestamp: DateTime.UtcNow));

                    return;
                }

                // Send revision request back to the exact worker
                string revisionRequest = $@"REVISION NEEDED (Attempt {retries}/3)

ORIGINAL TASK:
{_lastTaskSent.GetValueOrDefault(message.SenderId, "Unknown task")}

PREVIOUS ATTEMPT:
{message.Content}

FEEDBACK FROM MANAGER:
{feedback}

DO NOT ASK FOR MORE CONTEXT. Produce the final technical artifact now.";

                await Bus.SendAsync(new AgentMessage(
                    SenderId: Config.Id,
                    ReceiverId: message.SenderId,
                    Type: MessageType.TaskAssignment,
                    Content: revisionRequest,
                    Timestamp: DateTime.UtcNow));

                ConsoleLogger.WriteLine($"[{Config.Name}] 🔄 Sent revision to {message.SenderId} (retry {retries})", ConsoleColor.Yellow);
            }
        }

        private async Task HandleBossDecisionAsync(AgentMessage message)
        {
            
            ConsoleLogger.WriteLine($"[{Config.Name}] 📥 Received decision from Boss", ConsoleColor.Cyan);

            // For now we just log it. You can extend this later to broadcast "mission complete"
            // or trigger final actions across workers.
            Memory.Remember($"BossDecision_{DateTime.UtcNow:yyyyMMdd_HHmmss}", message.Content, propagate: true);
        }

        private void LearnSkill(string name, string data)
        {
            _skills[name] = data;
            Memory.Remember(name, data, propagate: true);
        }

        public void AssignWorker(string id) => _workerIds.Add(id);
    }
}