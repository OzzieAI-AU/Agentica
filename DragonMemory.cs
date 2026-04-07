namespace OzzieAI.Agentica
{
    using OzzieAI.Agentica.Providers;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A multi-layered memory architecture that manages conversation state, 
    /// ephemeral knowledge propagation, and physical file persistence.
    /// Acts as the 'Cerebellum' of the Agent, coordinating between active thought and stored facts.
    /// </summary>
    public class DragonMemory
    {
        /// <summary>
        /// Thread-safe key-value store for high-speed retrieval of transient facts or 
        /// state variables shared across the agent's current lifecycle.
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _ephemeralMemory = new();

        /// <summary>
        /// A hierarchy of parent memory instances. Knowledge 'propogated' from this 
        /// instance will flow upward to these upstreams (e.g., Worker to Manager).
        /// </summary>
        private readonly List<DragonMemory> _upstreams = new();

        /// <summary>
        /// The sequential record of the current conversation thread. 
        /// This list is what the LLM Provider actually 'reads' to maintain context.
        /// </summary>
        public List<IChatMessage> History { get; } = new();

        /// <summary>
        /// The physical disk-linked storage layer (LiveCache). 
        /// Allows the agent to "remember" code changes and project structures across sessions.
        /// </summary>
        public LiveCache? PersistentCache { get; private set; }

        /// <summary>
        /// Connects a physical <see cref="LiveCache"/> to this memory instance, 
        /// enabling long-term persistence for file-based knowledge.
        /// </summary>
        public void AttachPersistentCache(LiveCache cache) => PersistentCache = cache;

        /// <summary>
        /// Appends a new interaction (User, Assistant, or Tool) to the active context.
        /// Uses a thread-lock to ensure history integrity during rapid multi-agent updates.
        /// </summary>
        /// <param name="message">The message to be archived in the current session.</param>
        public void AddMessage(IChatMessage message)
        {
            lock (History)
            {
                History.Add(message);
            }
        }

        /// <summary>
        /// Establishes a hierarchical link to a parent memory. 
        /// Allows this agent to contribute its findings to a larger collective knowledge pool.
        /// </summary>
        public void AddUpstream(DragonMemory parentMemory)
        {
            lock (_upstreams)
            {
                if (!_upstreams.Contains(parentMemory)) _upstreams.Add(parentMemory);
            }
        }

        /// <summary>
        /// Stores a specific piece of information. 
        /// Automatically detects if the 'key' is a file path and mirrors the data to the 
        /// <see cref="PersistentCache"/> for long-term storage.
        /// </summary>
        /// <param name="key">The unique identifier or file path for the knowledge.</param>
        /// <param name="knowledge">The actual data or content to store.</param>
        /// <param name="propagate">If true, recursively pushes this knowledge to all upstream parents.</param>
        public void Remember(string key, string knowledge, bool propagate = true)
        {
            _ephemeralMemory[key] = knowledge;

            // Heuristic detection: If the key looks like a source file, we commit to disk.
            if (PersistentCache != null && (key.EndsWith(".cs") || key.Contains('/') || key.Contains('\\')))
            {
                // Score of 80 indicates high-confidence agent-generated content.
                PersistentCache.UpdateFileContentAndScore(key, knowledge, 80, "Auto-saved by DragonMemory");
            }

            if (propagate)
            {
                lock (_upstreams)
                {
                    foreach (var upstream in _upstreams)
                        upstream.Remember(key, knowledge, true);
                }
            }
        }

        /// <summary>
        /// Attempts to retrieve information by key.
        /// Priority: 1. Ephemeral Memory (Fastest) -> 2. Persistent Cache (Disk) -> 3. Fail.
        /// </summary>
        /// <param name="key">The identifier or file path to look up.</param>
        /// <returns>The stored string content or a failure message if not found.</returns>
        public string Recall(string key)
        {
            // Tier 1: Check high-speed RAM
            if (_ephemeralMemory.TryGetValue(key, out var val)) return val;

            // Tier 2: Check project-wide persistent cache
            var file = PersistentCache?.GetAllFiles().FirstOrDefault(f => f.FullPath == key);
            return file?.Content ?? "Memory not found.";
        }
    }
}