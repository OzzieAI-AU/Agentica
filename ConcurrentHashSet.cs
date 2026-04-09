using System.Collections.Concurrent;

namespace OzzieAI.Agentica
{

    /// <summary>
    /// Represents a dependency between two tasks in the agent swarm.
    /// </summary>
    // Simple thread-safe HashSet
    public class ConcurrentHashSet<T> : ConcurrentDictionary<T, bool>
    {
        public bool Add(T item) => TryAdd(item, true);
        public bool Contains(T item) => ContainsKey(item);
    }
}