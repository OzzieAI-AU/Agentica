using System;
using System.Text.Json;
using System.Threading.Tasks;
using OzzieAI.Agentica.Tools;

namespace OzzieAI.Agentica.Tools
{
    /// <summary>
    /// ✨ InformationDiscoveryTool (The "Gap Filler") ✨
    /// 
    /// This tool serves as a high-integrity bridge for when an agent identifies a knowledge 
    /// void during task execution. Instead of hallucinating or failing, the agent uses 
    /// this to perform a two-stage acquisition:
    /// 
    /// 1. TACTICAL SEARCH: Scours the web (via WebSearchTool) for real-time facts/docs.
    /// 2. HUMAN OVERRIDE: If the web is silent or the task is high-stakes, it prompts 
    ///    the human operator for the specific missing "key."
    /// 
    /// Use Case: Finding the latest version of a library, checking a specific API 
    /// documentation, or clarifying a vague requirement from the Boss.
    /// </summary>
    public class InformationDiscoveryTool : IAgentTool
    {
        private readonly WebSearchTool _searchEngine;

        /// <summary>
        /// Unique identifier used by the LLM to invoke the gap-filling sequence.
        /// </summary>
        public string Name => "gap_filler";

        /// <summary>
        /// The instruction manual for the LLM. It defines the 'When' and 'Why'.
        /// </summary>
        public string Description =>
            "MANDATORY: Use this when you encounter a knowledge gap, missing documentation, " +
            "or need to verify a fact before proceeding. This tool will search the web " +
            "and, if necessary, ask the user for clarification to ensure a 100% Perfection Score.";

        /// <summary>
        /// Initializes the tool with a reference to the existing WebSearch infrastructure.
        /// </summary>
        /// <param name="searchEngine">The active WebSearchTool instance for the agent.</param>
        public InformationDiscoveryTool(WebSearchTool searchEngine)
        {
            _searchEngine = searchEngine ?? throw new ArgumentNullException(nameof(searchEngine));
        }

        /// <summary>
        /// Executes the mid-task discovery logic.
        /// </summary>
        public async Task<string> ExecuteAsync(string jsonArguments)
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var args = JsonSerializer.Deserialize<DiscoveryArgs>(jsonArguments, options);

                if (args == null || string.IsNullOrWhiteSpace(args.Query))
                {
                    return "Error: You must provide a 'query' describing exactly what info is missing.";
                }

                // --- STAGE 1: WEB INVESTIGATION ---
                ConsoleLogger.WriteLine($"\n[GAP FILLER]: 🔍 Investigating knowledge gap: \"{args.Query}\"", ConsoleColor.Cyan);
                ConsoleLogger.WriteLine("[GAP FILLER]: Phase 1 - Scouring the web...", ConsoleColor.DarkGray);

                // Prepare the search payload for the existing WebSearchTool
                string searchPayload = JsonSerializer.Serialize(new { query = args.Query, combined = true });
                string webResults = await _searchEngine.ExecuteAsync(searchPayload);

                // --- STAGE 2: HUMAN INTERACTION (Conditional) ---
                string userClarification = "No human input requested for this discovery.";

                // Trigger human input if the web results are poor or if the agent explicitly requested it
                bool webSearchFailed = string.IsNullOrEmpty(webResults) || webResults.Contains("[SORRY]");

                if (args.AskUser || webSearchFailed)
                {
                    ConsoleLogger.WriteLine("\n[GAP FILLER]: Phase 2 - Requesting Human Intelligence.", ConsoleColor.Yellow);

                    if (webSearchFailed)
                        ConsoleLogger.WriteLine(" [NOTICE]: Web search returned no conclusive results.", ConsoleColor.DarkGray);

                    ConsoleLogger.WriteLine($" [CONTEXT]: {args.Explanation ?? "The agent needs more details to maintain accuracy."}");
                    Console.Write(" [OPERATOR RESPONSE]: ");

                    // Block and wait for human input (The "Mid-task pause")
                    var input = Console.ReadLine();
                    userClarification = !string.IsNullOrWhiteSpace(input) ? input : "User provided no additional details.";
                }

                // --- STAGE 3: CONSOLIDATION ---
                var finalReport = new
                {
                    DiscoveryStatus = "Gap Filled",
                    WebIntelligence = webResults,
                    HumanIntelligence = userClarification,
                    AcquisitionTime = DateTime.UtcNow
                };

                ConsoleLogger.WriteLine("[GAP FILLER]: ✅ Intelligence report generated. Resuming task...", ConsoleColor.Green);

                // Return a structured JSON report to the agent's memory
                return JsonSerializer.Serialize(finalReport, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                ConsoleLogger.WriteLine($"[GAP FILLER]: 💥 Critical failure during discovery: {ex.Message}", ConsoleColor.Red);
                return $"Discovery Failed: {ex.Message}. Please try an alternative query.";
            }
        }

        /// <summary>
        /// Defines the JSON Schema so the LLM knows how to fill the gaps.
        /// </summary>
        public object GetToolDefinition() => new
        {
            type = "function",
            function = new
            {
                name = Name,
                description = Description,
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        query = new
                        {
                            type = "string",
                            description = "The specific factual question or search term needed to fill the gap (e.g. 'latest version of Newtonsoft.Json')."
                        },
                        ask_user = new
                        {
                            type = "boolean",
                            description = "Set to true if this is a project-specific detail that only the user would know."
                        },
                        explanation = new
                        {
                            type = "string",
                            description = "A polite note to the user explaining WHY you need this information."
                        }
                    },
                    required = new[] { "query" }
                }
            }
        };

        /// <summary>
        /// Internal DTO for parsing the LLM's request.
        /// </summary>
        private class DiscoveryArgs
        {
            public string Query { get; set; } = string.Empty;
            public bool AskUser { get; set; } = false;
            public string? Explanation { get; set; }
        }
    }
}