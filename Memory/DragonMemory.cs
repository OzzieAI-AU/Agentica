// ========================================================
// FILE: DragonMemory.cs (FINAL - INTEGRATED)
// Beautifully documented, production-ready, no code duplicated
// ========================================================

namespace OzzieAI.Agentica
{
    using OzzieAI.Agentica.Providers;
    using OzzieAI.Agentica.Providers.Ollama;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// ✨ DragonMemory - The Complete Cerebellum Engine ✨
    ///
    /// <para>
    /// The central nervous system of every agent. Manages three memory tiers exactly as described
    /// in the official README treatise:
    /// 1. Ephemeral Memory (History) – active conversation sent to the LLM
    /// 2. Tactical Memory (Skills) – learned knowledge that bubbles up the hierarchy
    /// 3. Persistent Memory (LiveCache) – hard-drive cortex with perfection scoring
    /// </para>
    ///
    /// <para>
    /// Automatically performs "Context Conciliation" via <see cref="MemoryManager"/> when history grows,
    /// preventing token overflow while preserving mission anchors and recent context.
    /// </para>
    /// </summary>
    public sealed class DragonMemory
    {
        /// <summary>
        /// Ephemeral short-term memory – the exact history sent to the LLM.
        /// </summary>
        public List<IChatMessage> History { get; } = new();

        /// <summary>
        /// Tactical skills learned during this session (thread-safe).
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _tacticalSkills = new();

        /// <summary>
        /// Persistent hard-drive cortex with perfection scoring and file watching.
        /// </summary>
        public LiveCache? PersistentCache { get; private set; }

        /// <summary>
        /// Hierarchical upstream memory instances (Worker → Manager → Boss).
        /// Used for automatic knowledge propagation ("bubble up").
        /// </summary>
        private readonly List<DragonMemory> _upstreams = new();

        private readonly MemoryManager _memoryManager = new();
        private readonly string _agentId;

        /// <summary>
        /// Creates a fully wired DragonMemory instance.
        /// </summary>
        public DragonMemory(string agentId)
        {
            _agentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
        }

        /// <summary>
        /// Attaches the persistent LiveCache (called by AgentFactory).
        /// </summary>
        public void AttachPersistentCache(LiveCache cache) => PersistentCache = cache;

        /// <summary>
        /// Establishes a hierarchical link to a parent memory for upstream propagation.
        /// </summary>
        public void AddUpstream(DragonMemory parentMemory)
        {
            lock (_upstreams)
            {
                if (!_upstreams.Contains(parentMemory))
                    _upstreams.Add(parentMemory);
            }
        }

        /// <summary>
        /// Appends a new interaction to history and triggers automatic compression if needed.
        /// </summary>
        public async Task AddMessageAsync(IChatMessage message)
        {
            lock (History)
            {
                History.Add(message);
            }

            // Automatic Context Conciliation (README requirement)
            if (History.Count > 12) // Threshold from MemoryManager
            {
                // Use the fast local summarizer (Ollama) for compression
                var summarizer = new OllamaProvider("llama3.1"); // or whatever fast model you use
                var conciliated = await _memoryManager.ConciliateMemoryAsync(
                    History.Cast<ChatMessage>().ToList(), summarizer);

                History.Clear();
                History.AddRange(conciliated);
            }
        }

        /// <summary>
        /// Legacy sync wrapper for backward compatibility with existing BaseAgent calls.
        /// </summary>
        public void AddMessage(IChatMessage message)
        {
            // Fire-and-forget the async compression (non-blocking)
            _ = AddMessageAsync(message);
        }

        /// <summary>
        /// Core "Remember" method – stores knowledge and propagates upstream exactly as described in the README.
        /// </summary>
        /// <summary>
        /// Core "Remember" method – stores knowledge and propagates upstream exactly as described in the README.
        /// </summary>
        /// <param name="key">The identifier for the knowledge.</param>
        /// <param name="knowledge">The data or skill content.</param>
        /// <param name="propagate">Whether to bubble this knowledge up to the Manager/Boss.</param>
        public void Remember(string key, string knowledge, bool propagate = true)
        {

            _tacticalSkills[key] = knowledge;

            if (PersistentCache != null)
            {
                // CRITICAL FIX: If the key looks like a physical file, update the physical file.
                // Otherwise, save it directly into the JSON as a virtual tactical skill.
                if (key.EndsWith(".cs") || key.Contains('/') || key.Contains('\\'))
                {
                    PersistentCache.UpdateFileContentAndScore(key, knowledge, 80, "Auto-saved by DragonMemory");
                }
                else
                {
                    PersistentCache.SaveSkillToCache(key, knowledge, 80);
                }
            }

            if (propagate)
            {
                lock (_upstreams)
                {
                    foreach (var upstream in _upstreams)
                        upstream.Remember(key, knowledge, true);
                }
            }

            ConsoleLogger.WriteLine($"[DragonMemory {_agentId}] 💡 Remembered & persisted: {key}", ConsoleColor.DarkGreen);
        }

        /// <summary>
        /// Tiered recall: Ephemeral → Persistent Cache.
        /// </summary>
        public string? Recall(string key)
        {
            if (_tacticalSkills.TryGetValue(key, out var val))
                return val;

            return PersistentCache?.GetAllFiles()
                .FirstOrDefault(f => f.FullPath == key)?.Content;
        }

        /// <summary>
        /// Builds ASCII project map for Boss decision prompts (used in HandleDecisionRequestAsync).
        /// </summary>
        public string BuildAsciiGraph() => PersistentCache?.BuildAsciiGraph() ?? "No project artifacts yet.";
    }
}