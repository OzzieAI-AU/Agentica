namespace OzzieAI.Agentica
{
    using OzzieAI.Agentica.Providers;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// ManagerAgent - Tactical coordinator with robust task dependency support.
    /// 
    /// This agent intelligently manages task delegation to workers while respecting 
    /// explicit dependencies between tasks. It automatically queues tasks that have 
    /// unmet prerequisites and releases them as soon as all dependencies are satisfied.
    /// 
    /// All original functionality (worker delegation, result escalation to Boss, 
    /// skill learning, and rich logging) is fully preserved and enhanced.
    /// </summary>
    public class ManagerAgent : BaseAgent
    {
        /// <summary>
        /// Thread-safe collection of registered worker agent IDs available for task delegation.
        /// </summary>
        private readonly ConcurrentBag<string> _workerIds = new();

        /// <summary>
        /// Thread-safe dictionary storing skills and knowledge learned by this manager.
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _skills = new();

        /// <summary>
        /// Task dependency graph.
        /// Key   = Dependent Task ID
        /// Value = List of prerequisite Task IDs that must complete before this task can run.
        /// </summary>
        private readonly ConcurrentDictionary<string, List<string>> _taskDependencies = new();

        /// <summary>
        /// Set of task IDs that have been successfully completed.
        /// Used to evaluate whether dependent tasks are ready to be released.
        /// </summary>
        private readonly ConcurrentHashSet<string> _completedTasks = new();

        /// <summary>
        /// Queue of tasks that are currently blocked because their dependencies are not yet met.
        /// Key = Task ID, Value = Original message to be re-sent when the task becomes ready.
        /// </summary>
        private readonly ConcurrentDictionary<string, AgentMessage> _pendingTasks = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="ManagerAgent"/> class.
        /// </summary>
        /// <param name="config">The configuration settings for this manager agent.</param>
        /// <param name="bus">The agent message bus used for inter-agent communication.</param>
        /// <param name="memory">The shared memory instance for conversation history and knowledge storage.</param>
        public ManagerAgent(AgentConfig config, IAgentBus bus, DragonMemory memory)
            : base(config, bus, memory)
        {
        }

        /// <summary>
        /// Registers a worker agent with this manager, allowing it to receive delegated tasks.
        /// </summary>
        /// <param name="workerId">The unique identifier of the worker agent.</param>
        public void AssignWorker(string workerId) => _workerIds.Add(workerId);

        /// <summary>
        /// Adds a dependency relationship: the dependent task will wait until the prerequisite task completes successfully.
        /// </summary>
        /// <param name="prerequisiteTaskId">The task that must finish first.</param>
        /// <param name="dependentTaskId">The task that depends on the prerequisite task.</param>
        public void AddDependency(string prerequisiteTaskId, string dependentTaskId)
        {
            _taskDependencies.GetOrAdd(dependentTaskId, _ => new List<string>()).Add(prerequisiteTaskId);

            ConsoleLogger.WriteLine($"[Manager {Config.Name}] 🔗 Dependency added: {dependentTaskId} waits for {prerequisiteTaskId}", ConsoleColor.DarkYellow);
        }

        /// <summary>
        /// Main message handler. Routes incoming messages based on their type with full dependency awareness.
        /// </summary>
        /// <param name="message">The incoming message from the agent bus.</param>
        protected override async Task ProcessIncomingMessageAsync(AgentMessage message)
        {

            ConsoleLogger.WriteLine($"[Manager {Config.Name}] 📥 Received {message.Type} from {message.SenderId}", ConsoleColor.Magenta);
            
            // Record the interaction in the agent's memory for context in future reasoning
            Memory.History.Add(new ChatMessage
            {
                Role = message.Type == MessageType.TaskResult ? "user" : "assistant",
                Content = $"Received {message.Type} from {message.SenderId}: {message.Content}"
            });

            switch (message.Type)
            {
                case MessageType.TaskAssignment:
                    await HandleTaskAssignmentAsync(message);
                    break;

                case MessageType.TaskResult:
                    await HandleTaskResultAsync(message);
                    break;

                case MessageType.KnowledgeTransfer:
                    var skillKey = $"Skill_{DateTime.UtcNow:yyyyMMdd}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";
                    LearnSkill(skillKey, message.Content);
                    break;
            }
        }

        /// <summary>
        /// Handles a new task assignment. Checks dependencies and either delegates immediately 
        /// or queues the task as pending if prerequisites are not yet met.
        /// </summary>
        /// <param name="message">The task assignment message.</param>
        private async Task HandleTaskAssignmentAsync(AgentMessage message)
        {
            // Generate a short, consistent task ID
            string taskId = Guid.NewGuid().ToString("N").Substring(0, 8);

            ConsoleLogger.WriteLine($"[Manager {Config.Name}] 🎯 New task assigned (ID: {taskId})", ConsoleColor.Cyan);

            // Check if all prerequisites for this task have been completed
            if (!ArePrerequisitesMet(taskId))
            {

                ConsoleLogger.WriteLine($"[Manager {Config.Name}] ⏳ Task {taskId} has unmet dependencies → queued", ConsoleColor.Red);

                _pendingTasks[taskId] = message;
                return;
            }

            // All prerequisites are satisfied — delegate to a worker
            await DelegateToAvailableWorkerAsync(message, taskId);
        }

        /// <summary>
        /// Handles a task completion result from a worker.
        /// Marks the task as completed, releases any dependent tasks that are now ready,
        /// and escalates the result to the Boss agent.
        /// </summary>
        /// <param name="message">The task result message.</param>
        private async Task HandleTaskResultAsync(AgentMessage message)
        {
            string taskId = message.Id ?? Guid.NewGuid().ToString("N").Substring(0, 8);

            ConsoleLogger.WriteLine($"[Manager {Config.Name}] ✅ Task completed: {taskId}", ConsoleColor.Green);

            // Record this task as completed
            _completedTasks.Add(taskId);

            // Release any dependent tasks that were waiting for this one
            await ReleaseReadyDependentTasksAsync(taskId);

            // Escalate summary result to the Boss/Parent agent
            string bossId = Config.ParentId ?? "Ozzie-CEO";
            await Bus.SendAsync(new AgentMessage(
                Config.Id,
                bossId,
                MessageType.DecisionRequest,
                $"Task {taskId} completed by worker.\nSummary: {message.Content}",
                DateTime.Now));
        }

        /// <summary>
        /// Delegates a task to an available worker agent.
        /// </summary>
        /// <param name="message">The original task message.</param>
        /// <param name="taskId">The unique ID assigned to this task.</param>
        private async Task DelegateToAvailableWorkerAsync(AgentMessage message, string taskId)
        {
            if (_workerIds.TryPeek(out var workerId))
            {

                ConsoleLogger.WriteLine($"[Manager {Config.Name}] → Delegating task {taskId} to worker {workerId}", ConsoleColor.Blue);
                
                // Attach the task ID to the message before sending
                var messageWithId = message with { Id = taskId };

                await Bus.SendAsync(messageWithId);
            }
            else
            {

                ConsoleLogger.WriteLine($"[Manager {Config.Name}] ⚠️ No workers available for task {taskId}", ConsoleColor.Red);
            }
        }

        /// <summary>
        /// Checks whether all prerequisite tasks for the given task have been completed.
        /// </summary>
        /// <param name="taskId">The task ID to check.</param>
        /// <returns>True if the task has no dependencies or all prerequisites are completed.</returns>
        private bool ArePrerequisitesMet(string taskId)
        {
            if (!_taskDependencies.TryGetValue(taskId, out var prereqs) || prereqs.Count == 0)
                return true; // No dependencies = ready to execute

            return prereqs.All(p => _completedTasks.Contains(p));
        }

        /// <summary>
        /// Scans the pending tasks queue and releases any tasks whose dependencies have now been satisfied.
        /// </summary>
        /// <param name="justCompletedTaskId">The task that just finished, potentially unblocking others.</param>
        private async Task ReleaseReadyDependentTasksAsync(string justCompletedTaskId)
        {
            // Find all pending tasks that are now ready to run
            var readyTasks = _pendingTasks.Keys
                .Where(tid => ArePrerequisitesMet(tid))
                .ToList();

            foreach (var taskId in readyTasks)
            {
                if (_pendingTasks.TryRemove(taskId, out var pendingMessage))
                {

                    ConsoleLogger.WriteLine($"[Manager {Config.Name}] 🔓 Releasing dependent task {taskId} (dependencies met)", ConsoleColor.Yellow);
                    
                    await DelegateToAvailableWorkerAsync(pendingMessage, taskId);
                }
            }
        }

        /// <summary>
        /// Stores a newly learned skill both locally and in persistent memory.
        /// </summary>
        /// <param name="skillName">Unique name/identifier for the skill.</param>
        /// <param name="skillData">The actual content or data of the skill.</param>
        private void LearnSkill(string skillName, string skillData)
        {
            _skills[skillName] = skillData;
            Memory.Remember(skillName, skillData, propagate: true);

            ConsoleLogger.WriteLine($"[Manager {Config.Name}] 💡 Learned skill: {skillName}", ConsoleColor.DarkCyan);
        }
    }
}