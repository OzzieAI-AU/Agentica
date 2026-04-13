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
    using System.Text;
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
        public DragonMemory(string agentId, LiveCache cache)
        {
            // 
            _agentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
            PersistentCache = cache;
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
                var summarizer = new OllamaProvider("gemma4:e2b"); // or whatever fast model you use
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
        /// Performs a tiered lookup for information. 
        /// First checks tactical (learned) skills, then falls back to the persistent file cache.
        /// </summary>
        /// <param name="query">The keyword or file path to recall.</param>
        /// <returns>The content if found; otherwise, null.</returns>
        public string? Recall(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;

            // Tier 1: Tactical Memory (Fastest)
            // Look for partial matches in learned skill keys
            var skillKey = _tacticalSkills.Keys.FirstOrDefault(k =>
                query.Contains(k, StringComparison.OrdinalIgnoreCase));

            if (skillKey != null) return _tacticalSkills[skillKey];

            // Tier 2: Persistent Cache (File System)
            var file = PersistentCache.GetAllFiles().FirstOrDefault(f =>
                f.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                f.FullPath.EndsWith(query));

            return file?.Content;
        }

        /// <summary>
        /// Generates a visual ASCII representation of the project hierarchy.
        /// Used by the Boss agent to understand the 'State of the Union'.
        /// </summary>
        public string BuildAsciiGraph()
        {
            var files = PersistentCache.GetAllFiles();
            if (!files.Any()) return "Memory Repository: [Empty]";

            var sb = new StringBuilder();
            sb.AppendLine("═══ PROJECT COGNITIVE MAP ═══");

            // Group by extension to show logical segments
            var groups = files.GroupBy(f => f.Extension);
            foreach (var group in groups)
            {
                sb.AppendLine($"Folder: [{group.Key.ToUpper().Replace(".", "")} Artifacts]");
                foreach (var file in group)
                {
                    string status = file.SizeBytes > 1000 ? "●" : "○";
                    sb.AppendLine($"  └── {status} {file.FileName} ({file.SizeBytes} bytes)");
                }
            }
            sb.AppendLine("═════════════════════════════");
            return sb.ToString();
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
    }
}