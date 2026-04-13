using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OzzieAI.Agentica
{

    /// <summary>
    /// Represents a single file entry in the LiveCache with rich metadata.
    /// Supports scoring for LLM-driven code perfection workflows.
    /// </summary>
    public class CachedFile
    {
        /// <summary>
        /// Full absolute path to the file (unique key).
        /// </summary>
        [JsonPropertyName("fullPath")]
        public string FullPath { get; set; } = string.Empty;

        /// <summary>
        /// File name only (without path).
        /// </summary>
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// File extension in lowercase (e.g. ".cs").
        /// </summary>
        [JsonPropertyName("extension")]
        public string Extension { get; set; } = string.Empty;

        /// <summary>
        /// File size in bytes.
        /// </summary>
        [JsonPropertyName("sizeBytes")]
        public long SizeBytes { get; set; }

        /// <summary>
        /// Last modified timestamp from the file system.
        /// </summary>
        [JsonPropertyName("lastModified")]
        public DateTime LastModified { get; set; }

        /// <summary>
        /// How many times the LLM has improved this file in the current session.
        /// </summary>
        [JsonPropertyName("fixCount")]
        public int FixCount { get; set; } = 0;

        /// <summary>
        /// UTC timestamp of the last LLM improvement (cooldown protection).
        /// </summary>
        [JsonPropertyName("lastImproved")]
        public DateTime LastImproved { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Creation timestamp from the file system.
        /// </summary>
        [JsonPropertyName("created")]
        public DateTime Created { get; set; }

        /// <summary>
        /// File attributes (ReadOnly, Hidden, etc.).
        /// </summary>
        [JsonPropertyName("attributes")]
        public FileAttributes Attributes { get; set; }

        /// <summary>
        /// True if the file is treated as readable text (code/config/docs).
        /// </summary>
        [JsonPropertyName("isText")]
        public bool IsText { get; set; }

        /// <summary>
        /// The actual text content of the file (only for text files; large files may be truncated).
        /// </summary>
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// LLM-assigned quality/perfection score (0-100). Updated automatically when content changes via LLM.
        /// </summary>
        [JsonPropertyName("score")]
        public int Score { get; set; } = 0;

        /// <summary>
        /// Optional notes from the LLM or user about this file's quality, issues, or improvements.
        /// </summary>
        [JsonPropertyName("notes")]
        public string Notes { get; set; } = string.Empty;

        /// <summary>
        /// Formatted human-readable size (e.g. "2.45 MB").
        /// </summary>
        [JsonIgnore]
        public string FormattedSize => FormatSize(SizeBytes);

        private static string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (number >= 1024 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:0.##} {suffixes[counter]}";
        }

        /// <summary>
        /// Updates the file from disk (refreshes metadata and content if text).
        /// </summary>
        public void RefreshFromDisk()
        {
            if (!File.Exists(FullPath)) return;

            var fi = new FileInfo(FullPath);
            FullPath = fi.FullName;
            FileName = fi.Name;
            Extension = fi.Extension.ToLowerInvariant();
            SizeBytes = fi.Length;
            LastModified = fi.LastWriteTime;
            Created = fi.CreationTime;
            Attributes = fi.Attributes;

            // Determine if text (smart detection)
            IsText = IsLikelyTextFile(FullPath);

            if (IsText && SizeBytes <= 2 * 1024 * 1024) // 2MB safety cap
            {
                try
                {
                    Content = File.ReadAllText(FullPath, Encoding.UTF8).TrimEnd();
                }
                catch
                {
                    Content = "[Could not read as text]";
                }
            }
            else if (IsText)
            {
                Content = "[File too large – content truncated]";
            }
            else
            {
                Content = "[Binary file – content not loaded]";
            }
        }

        private static bool IsLikelyTextFile(string fullPath)
        {
            string ext = Path.GetExtension(fullPath).ToLowerInvariant();
            var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".cs", ".csproj", ".sln", ".txt", ".md", ".json", ".xml", ".html", ".htm",
                ".js", ".ts", ".py", ".cu", ".c", ".cpp", ".h", ".hpp", ".java", ".sql",
                ".yaml", ".yml", ".toml", ".ini", ".cfg", ".log", ".csv", ".bat", ".sh", ".ps1"
            };

            if (textExtensions.Contains(ext)) return true;

            // Binary check via first 4KB
            try
            {
                using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                byte[] buffer = new byte[4096];
                int bytesRead = fs.Read(buffer, 0, buffer.Length);
                for (int i = 0; i < bytesRead; i++)
                {
                    byte b = buffer[i];
                    if (b == 0 || b == 127 || (b < 9 && b != '\t' && b != '\r' && b != '\n'))
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// LiveCache – A thread-safe, self-building, persistent cache of a directory structure.
    /// Builds a beautiful ASCII tree graph on demand. Supports per-file scoring and live updates.
    /// Persists using System.Text.Json for modern .NET compatibility.
    /// </summary>
    public class LiveCache : IDisposable
    {


        private readonly string _rootDirectory;
        private readonly string _cacheFilePath;


        /// <summary>
        /// Returns the root directory this LiveCache is monitoring.
        /// </summary>
        public string GetRootDirectory() => _rootDirectory;

        /// <summary>
        /// Thread-safe dictionary: FullPath → CachedFile.
        /// </summary>
        private readonly ConcurrentDictionary<string, CachedFile> _files = new(StringComparer.OrdinalIgnoreCase);

        // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
        // THREAD-SAFE PERSISTENCE ENGINE (IRONCLAD)
        // ──────────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A modern, async-friendly lock. Ensures absolutely only ONE background task can write at a time.
        /// </summary>
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Volatile ensures that all threads immediately see the updated state of this flag,
        /// preventing CPU-cache synchronization issues.
        /// </summary>
        private volatile bool _saveQueued = false;

        /// <summary>
        /// Signals the cancellation of pending background saves when the application is shutting down.
        /// </summary>
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        /// <summary>
        /// A state flag indicating whether a background save operation is already queued.
        /// This acts as a throttle to prevent "Task Storms" during high-frequency updates.
        /// </summary>
        private bool _isSavePending = false;


        private bool _isWorkerActive = false;
        private readonly object _workerLock = new object();

        private FileSystemWatcher? _watcher;
        private readonly object _lock = new object();
        private bool IncludeFileContents = true;
        private bool _disposed;

        /// <summary>
        /// Event raised when any file is added, modified, removed, or its score/content changes.
        /// </summary>
        public event EventHandler? OnCacheChanged;


        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Creates a new LiveCache for the specified root directory.
        /// Cache is persisted as ".livecache.json" inside the root folder.
        /// </summary>
        public LiveCache(string rootDirectory)
        {

            if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
                Directory.CreateDirectory(rootDirectory);

            _rootDirectory = Path.GetFullPath(rootDirectory);
            _cacheFilePath = Path.Combine(_rootDirectory, ".livecache.json");

            LoadFromDisk();
            InitializeWatcher();
        }

        private void LoadFromDisk()
        {

            if (!File.Exists(_cacheFilePath)) return;

            try
            {
                string json = File.ReadAllText(_cacheFilePath);
                var loadedFiles = JsonSerializer.Deserialize<List<CachedFile>>(json, _jsonOptions);

                if (loadedFiles != null)
                {
                    foreach (var file in loadedFiles)
                    {
                        if (File.Exists(file.FullPath))
                        {
                            file.RefreshFromDisk();
                            _files[file.FullPath] = file;
                        }
                        // CRITICAL FIX: Re-hydrate virtual skills that are not physical files.
                        // Real files have rooted paths (e.g., C:\...), while skill keys do not.
                        else if (!Path.IsPathRooted(file.FullPath))
                        {
                            _files[file.FullPath] = file;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to load LiveCache from disk: {ex.Message}");
            }
        }

        // ──────────────────────────────────────────────────────────────────────────────────────────────────────────
        // THREAD-SAFE & ANTIVIRUS-PROOF PERSISTENCE ENGINE
        // ──────────────────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Triggers a debounced background save operation. 
        /// Guarantees only one background worker is ever active.
        /// </summary>
        public void SaveToDisk()
        {
            _saveQueued = true;

            // Strict lock to ensure we never spawn overlapping background tasks
            lock (_workerLock)
            {
                if (_isWorkerActive) return;
                _isWorkerActive = true;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    while (_saveQueued)
                    {
                        // Reset flag before working so new requests can queue up again
                        _saveQueued = false;

                        // DEBOUNCE: Wait 500ms to batch multiple rapid changes
                        await Task.Delay(500, _cts.Token);

                        await WriteDirectlyAsync();
                    }
                }
                catch (OperationCanceledException) { /* App shutting down */ }
                finally
                {
                    lock (_workerLock)
                    {
                        _isWorkerActive = false;
                        // Double-check: If a request came in exactly as we were exiting, restart the worker
                        if (_saveQueued) SaveToDisk();
                    }
                }
            });
        }

        /// <summary>
        /// Writes directly to the JSON file using FileShare.Read to bypass Antivirus and IDE file locks.
        /// </summary>
        private async Task WriteDirectlyAsync()
        {
            const int MaxRetries = 5;
            int attempt = 0;

            // Snapshot the dictionary values so the JSON Serializer doesn't crash if an agent 
            // adds a new file at the exact millisecond serialization is running.
            var snapshot = _files.Values.ToList();

            while (attempt < MaxRetries)
            {
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(snapshot, options);

                    // CRITICAL FIX: Write directly using a FileStream with FileShare.Read.
                    // This prevents Windows Defender or VS Code from throwing "File in use" errors.
                    using (var fs = new FileStream(_cacheFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(fs))
                    {
                        await writer.WriteAsync(json);
                    }

                    return; // Success! Exit cleanly.
                }
                catch (IOException)
                {
                    attempt++;
                    if (attempt >= MaxRetries)
                    {
                        // If it STILL fails 5 times, a process has a hard Write-Lock on it.
                        ConsoleLogger.WriteLine($"[LiveCache] WARNING: File hard-locked by external process. Changes kept in memory.", ConsoleColor.DarkYellow);
                        return;
                    }

                    // Progressive backoff: 300ms, 600ms, 900ms...
                    await Task.Delay(300 * attempt);
                }
            }
        }

        /// <summary>
        /// Persists non-file tactical knowledge (Skills) directly into the LiveCache JSON.
        /// This creates a virtual file entry, fulfilling the 'External Cortex' requirement 
        /// to ensure hard-won technical discoveries survive system reboots.
        /// </summary>
        /// <param name="key">The unique identifier for the learned skill.</param>
        /// <param name="content">The actual tactical knowledge to persist.</param>
        /// <param name="score">The perfection score assigned to the knowledge (0-100).</param>
        public void SaveSkillToCache(string key, string content, int score)
        {

            lock (_lock)
            {
                // Treat the skill key as a virtual pseudo-path
                var virtualFile = _files.GetOrAdd(key, k => new CachedFile
                {
                    FullPath = k,
                    FileName = k,
                    IsText = true,
                    Created = DateTime.UtcNow
                });

                virtualFile.Content = content;
                virtualFile.Score = Math.Clamp(score, 0, 100);
                virtualFile.LastModified = DateTime.UtcNow;
                virtualFile.Notes = "Auto-saved Tactical Skill";

                OnCacheChanged?.Invoke(this, EventArgs.Empty);
                SaveToDisk();
            }
        }

        private bool IsNoisyFolder(string folderName)
        {
            string name = folderName.ToLowerInvariant();
            return name == "obj" || name == "bin" || name == "debug" || name == "release" ||
                   name.StartsWith(".") || name == ".livecache.json";
        }

        private void InitializeWatcher()
        {
            _watcher = new FileSystemWatcher(_rootDirectory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size |
                               NotifyFilters.CreationTime,
                Filter = "*.*",
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileChanged;
            _watcher.Changed += OnFileChanged;
            _watcher.Deleted += OnFileDeleted;
            _watcher.Renamed += OnFileRenamed;
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                if ((e.ChangeType == WatcherChangeTypes.Changed || e.ChangeType == WatcherChangeTypes.Created)
                    && File.Exists(e.FullPath))
                {
                    var cached = new CachedFile { FullPath = e.FullPath };
                    cached.RefreshFromDisk();
                    _files[e.FullPath] = cached;

                    OnCacheChanged?.Invoke(this, EventArgs.Empty);
                    SaveToDisk();
                }
            }
            catch { /* Ignore transient filesystem errors */ }
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            if (_files.TryRemove(e.FullPath, out _))
            {
                OnCacheChanged?.Invoke(this, EventArgs.Empty);
                SaveToDisk();
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            if (_files.TryRemove(e.OldFullPath, out var file))
            {
                file.FullPath = e.FullPath;
                file.RefreshFromDisk();
                _files[e.FullPath] = file;

                OnCacheChanged?.Invoke(this, EventArgs.Empty);
                SaveToDisk();
            }
        }

        /// <summary>
        /// Manually add or refresh a file in the cache.
        /// </summary>
        public void AddOrUpdateFile(string fullPath)
        {
            if (!File.Exists(fullPath)) return;

            var cached = new CachedFile { FullPath = fullPath };
            cached.RefreshFromDisk();
            _files[fullPath] = cached;

            OnCacheChanged?.Invoke(this, EventArgs.Empty);
            SaveToDisk();
        }

        /// <summary>
        /// Builds a clean ASCII tree graph (no full content included – perfect for LLM prompts).
        /// </summary>
        public string BuildAsciiGraph()
        {
            if (_files.IsEmpty)
                return "No files in LiveCache.";

            var sb = new StringBuilder();
            sb.AppendLine("```ascii");
            sb.AppendLine($"LIVECACHE PROJECT TREE – Root: {_rootDirectory}");
            sb.AppendLine("────────────────────────────────────────────────────────────");
            sb.AppendLine($"Total files: {_files.Count} | Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            // Start building from root (no prefix, root is not shown as a folder entry)
            BuildTreeRecursive(_rootDirectory, "", true, sb);

            sb.AppendLine("```");
            return sb.ToString();
        }

        private void BuildTreeRecursive(string currentDir, string prefix, bool isLastDir, StringBuilder sb)
        {
            // Get direct children: subdirectories + files in this folder (that are cached)
            var filesInDir = _files.Values
                .Where(f => string.Equals(Path.GetDirectoryName(f.FullPath), currentDir, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var subDirNames = _files.Values
                .Select(f => Path.GetDirectoryName(f.FullPath))
                .Where(dir => dir != null
                           && dir.StartsWith(currentDir, StringComparison.OrdinalIgnoreCase)
                           && !string.Equals(dir, currentDir, StringComparison.OrdinalIgnoreCase))
                .Select(dir => GetImmediateSubDir(currentDir, dir!))
                .Where(sub => sub != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Combine folders first, then files
            var entries = new List<(string Name, bool IsDirectory)>();
            foreach (var d in subDirNames)
                entries.Add((d!, true));

            foreach (var f in filesInDir)
                entries.Add((f.FileName, false));

            // Sort: directories before files, then alphabetically
            entries = entries
                .DistinctBy(e => e.Name)
                .OrderBy(e => !e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int i = 0; i < entries.Count; i++)
            {
                var (name, isDirectory) = entries[i];
                bool isLastEntry = i == entries.Count - 1;

                // Connector for current level
                string connector = isLastEntry ? "└── " : "├── ";

                // Next prefix: continue vertical line if not the last sibling
                string nextPrefix = prefix + (isLastEntry ? "    " : "│   ");

                if (isDirectory)
                {
                    // Folder line
                    sb.AppendLine($"{prefix}{connector}📁 {name}/");

                    string fullSubDir = Path.Combine(currentDir, name);
                    BuildTreeRecursive(fullSubDir, nextPrefix, isLastEntry, sb);
                }
                else
                {
                    // Find the cached file
                    var file = _files.Values.FirstOrDefault(f =>
                        string.Equals(f.FileName, name, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(Path.GetDirectoryName(f.FullPath), currentDir, StringComparison.OrdinalIgnoreCase));

                    if (file == null) continue;

                    // File line + metadata with proper indentation
                    sb.AppendLine($"{prefix}{connector}📄 {file.FileName} [Score: {file.Score}/100]");

                    string metaPrefix = nextPrefix;  // Use nextPrefix so vertical line continues under the file

                    sb.AppendLine($"{metaPrefix}Path      : {file.FullPath}");
                    sb.AppendLine($"{metaPrefix}Size      : {file.FormattedSize}");
                    sb.AppendLine($"{metaPrefix}Modified  : {file.LastModified:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine($"{metaPrefix}Created   : {file.Created:yyyy-MM-dd HH:mm:ss}");
                    sb.AppendLine($"{metaPrefix}Type      : {(file.IsText ? "Text" : "Binary")}");
                    if (IncludeFileContents)
                        sb.AppendLine($"{metaPrefix}Content     : {file.Content}");
                    if (!string.IsNullOrWhiteSpace(file.Notes))
                        sb.AppendLine($"{metaPrefix}Notes     : {file.Notes}");

                    sb.AppendLine(); // Empty line after each file for readability
                }
            }
        }

        private string? GetImmediateSubDir(string parent, string child)
        {
            if (!child.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) return null;
            string relative = child.Substring(parent.Length).Trim(Path.DirectorySeparatorChar, '/');
            if (string.IsNullOrEmpty(relative)) return null;
            return relative.Split(new[] { Path.DirectorySeparatorChar, '/' }, 2)[0];
        }

        /// <summary>
        /// Returns FileName → Content dictionary for easy LLM consumption.
        /// </summary>
        public Dictionary<string, string> GetNameToContentDictionary()
        {
            return _files.Values
                .Where(f => f.IsText && !string.IsNullOrWhiteSpace(f.Content))
                .ToDictionary(f => f.FileName, f => f.Content, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// HARDENED: Updates file ONLY if the new code is syntactically valid C# and appears complete.
        /// Never writes incomplete/truncated code to disk.
        /// </summary>
        public void UpdateFileContentAndScore(string fullPath, string? newContent, int newScore, string? notes = null)
        {

            if (_files.TryGetValue(fullPath, out var file) == false)
                return;

            lock (_lock)
            {
                bool shouldWrite = false;
                string finalContent = file.Content; // fallback to original

                if (!string.IsNullOrWhiteSpace(newContent))
                {
                    // === SAFETY GATE 1: Basic truncation / incompleteness check ===
                    if (IsObviouslyIncomplete(newContent))
                    {
                        LogToUI($"⛔ Skipped save for {Path.GetFileName(fullPath)} - detected incomplete code (truncation markers).");
                        newContent = null; // don't use it
                    }
                    else
                    {
                        // === SAFETY GATE 2: Roslyn syntax validation ===
                        if (IsValidCompleteCSharp(newContent, out string validationMessage))
                        {
                            finalContent = newContent.TrimEnd();
                            shouldWrite = true;

                            file.LastImproved = DateTime.UtcNow;
                            file.FixCount++;
                        }
                        else
                        {
                            LogToUI($"⛔ Skipped save for {Path.GetFileName(fullPath)} - syntax/validation failed: {validationMessage}");
                        }
                    }
                }

                // Always update metadata (score/notes) even if content wasn't written
                file.Content = finalContent;
                file.Score = Math.Clamp(newScore, 0, 100);
                if (!string.IsNullOrWhiteSpace(notes))
                    file.Notes = notes;

                if (shouldWrite && file.IsText && File.Exists(fullPath))
                {
                    try
                    {
                        File.WriteAllText(fullPath, finalContent, Encoding.UTF8);
                        file.RefreshFromDisk(); // re-read metadata after write
                        LogToUI($"✅ Safely updated: {Path.GetFileName(fullPath)} → {newScore}/100 (Fix #{file.FixCount})");
                    }
                    catch (Exception ex)
                    {
                        LogToUI($"❌ Disk write failed for {Path.GetFileName(fullPath)}: {ex.Message}");
                    }
                }
                else if (!shouldWrite && !string.IsNullOrWhiteSpace(newContent))
                {
                    LogToUI($"⚠️ Content update skipped for safety - file left unchanged.");
                }

                OnCacheChanged?.Invoke(this, EventArgs.Empty);
                
                // AUTONOMOUS TRIGGER: Fire-and-forget save to ensure disk integrity 
                // without blocking the agent's reasoning loop.
                _ = Task.Run(() => SaveToDisk());

                ConsoleLogger.WriteLine($"[LiveCache] 💾 Auto-persisted file: {file.FileName} (Score: {file.Score})", ConsoleColor.DarkCyan);
            }
        }

        private bool IsObviouslyIncomplete(string code)
        {

            code = code.TrimEnd();
            if (string.IsNullOrWhiteSpace(code)) return true;

            // Common truncation signs from LLMs
            return code.EndsWith("...") ||
                   code.EndsWith("…") ||
                   code.EndsWith("// TODO") ||
                   code.EndsWith("NotImplementedException") ||
                   code.Count(c => c == '{') != code.Count(c => c == '}') || // basic brace balance
                   code.EndsWith("partial class") || // suspicious incomplete
                   code.Length < 50; // too short to be meaningful
        }

        private bool IsValidCompleteCSharp(string code, out string message)
        {

            message = "OK";
            try
            {
                var tree = CSharpSyntaxTree.ParseText(code);

                var diagnostics = tree.GetDiagnostics()
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .ToList();

                if (diagnostics.Any())
                {
                    message = $"Syntax errors: {string.Join("; ", diagnostics.Select(d => d.GetMessage()))}";
                    return false;
                }

                // Extra heuristic: must contain at least one namespace/class (basic completeness)
                var root = tree.GetRoot();
                if (!root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.NamespaceDeclarationSyntax>().Any() &&
                    !root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().Any())
                {
                    message = "No namespace or class declaration found - likely incomplete snippet";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                message = $"Parse exception: {ex.Message}";
                return false;
            }
        }

        private void LogToUI(string text)
        {
            // Safe cross-thread logging (if you have rtbMain accessible, or raise event)
            // For now, Console + optional event
            Console.WriteLine(text);
            // You can add an event for UI if desired
        }

        public void Clear()
        {
            _files.Clear();
            if (File.Exists(_cacheFilePath))
                File.Delete(_cacheFilePath);

            OnCacheChanged?.Invoke(this, EventArgs.Empty);
        }

        public IReadOnlyList<CachedFile> GetAllFiles() => _files.Values.ToList().AsReadOnly();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _watcher?.Dispose();
            SaveToDisk();
        }
    }
}