namespace OzzieAI.Agentica.Tools
{
    using Microsoft.Web.WebView2.Core;
    using Microsoft.Web.WebView2.WinForms;
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows.Forms;
    using Application = System.Windows.Forms.Application;

    /// <summary>
    /// ✨ MAGIC WEB SEARCH TOOL ✨
    /// 
    /// This is like giving your AI agent a pair of invisible eyes that can look up anything on the internet.
    /// It uses a real browser (WebView2) running in secret (headless mode) so websites don't know it's a robot.
    /// 
    /// Features:
    /// • Supports Google, Bing, and DuckDuckGo
    /// • Can search one engine or combine all three (default = combine)
    /// • Returns clean, useful results with title, URL, and snippet
    /// • Designed to be reliable even when websites try to block robots
    /// 
    /// Every part is explained like a story so even a curious beginner can understand why each line exists.
    /// </summary>
    public class WebSearchTool : IAgentTool
    {
        /// <summary>
        /// The name the AI uses when it wants to search the web.
        /// </summary>
        public string Name => "web_search";

        /// <summary>
        /// Friendly description that tells the AI what this tool can do.
        /// </summary>
        public string Description =>
            "Performs a deep-web search using a real invisible browser (WebView2). " +
            "Supports Google, Bing, and DuckDuckGo. Can combine results from all three for better answers.";

        /// <summary>
        /// Tells the AI exactly what parameters it needs to provide when calling this tool.
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
                            description = "The search query (what you want to look up)."
                        },
                        provider = new
                        {
                            type = "string",
                            description = "Which search engine to use: Google (default), Bing, or DuckDuckGo. Ignored when combined=true.",
                            @enum = new[] { "Google", "Bing", "DuckDuckGo" }
                        },
                        combined = new
                        {
                            type = "boolean",
                            description = "true (default) = search ALL three engines and combine results. false = use only the chosen provider."
                        }
                    },
                    required = new[] { "query" }
                }
            }
        };

        /// <summary>
        /// This is where the AI calls the tool. We read the JSON, set defaults, and start the invisible browser.
        /// </summary>
        public Task<string> ExecuteAsync(string jsonArguments)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var args = JsonSerializer.Deserialize<SearchArgs>(jsonArguments, options);

            // Safety check
            if (string.IsNullOrWhiteSpace(args?.Query))
                return Task.FromResult("Error: Empty query. Please tell me what you want to search for!");

            // Friendly defaults
            string provider = string.IsNullOrWhiteSpace(args.Provider) ? "Google" : args.Provider.Trim();
            if (!AvailableProviders.ContainsKey(provider))
                provider = "Google";

            bool combined = args.Combined; // default is true

            var tcs = new TaskCompletionSource<string>();

            // WebView2 must run on a Single-Threaded Apartment (STA) thread
            var thread = new Thread(() =>
            {
                try
                {
                    RunHeadlessBrowser(args.Query, provider, combined, tcs);
                    Application.Run(); // Start the message pump required by WebView2
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                    Application.ExitThread();
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            return tcs.Task;
        }

        // ====================================================================
        // PRIVATE HELPERS (the secret ingredients)
        // ====================================================================

        /// <summary>
        /// Holds configuration for each search engine (name, URL template, and JavaScript scraper).
        /// </summary>
        private sealed class ProviderConfig
        {
            public string Name { get; set; } = string.Empty;
            public string SearchUrlTemplate { get; set; } = string.Empty;
            public string JsExtractor { get; set; } = string.Empty;
        }

        /// <summary>
        /// The three supported search engines. DuckDuckGo is preferred because it's the most stable for scraping.
        /// </summary>
        private static readonly Dictionary<string, ProviderConfig> AvailableProviders = new()
        {
            { "Google", new ProviderConfig
                {
                    Name = "Google",
                    SearchUrlTemplate = "https://www.google.com/search?q={0}",
                    JsExtractor = GoogleJsExtractor
                }
            },
            { "Bing", new ProviderConfig
                {
                    Name = "Bing",
                    SearchUrlTemplate = "https://www.bing.com/search?q={0}",
                    JsExtractor = BingJsExtractor
                }
            },
            { "DuckDuckGo", new ProviderConfig
                {
                    Name = "DuckDuckGo",
                    SearchUrlTemplate = "https://html.duckduckgo.com/html/?q={0}",
                    JsExtractor = DdgJsExtractor
                }
            }
        };

        // ====================================================================
        // JAVASCRIPT SCRAPERS (the "robots" that read the webpage)
        // ====================================================================

        /// <summary>
        /// JavaScript that extracts search results from Google.
        /// Made more resilient to Google's changing page structure.
        /// </summary>
        private const string GoogleJsExtractor = @"
            (() => {
                let results = [];
                let nodes = document.querySelectorAll('div.g');
                for (let i = 0; i < Math.min(nodes.length, 7); i++) {
                    let titleEl = nodes[i].querySelector('h3');
                    let title = titleEl ? titleEl.innerText.trim() : '';
                    let url = titleEl && titleEl.closest('a') ? titleEl.closest('a').href : '';
                    let snippetEl = nodes[i].querySelector('.VwiC3b, .s3v9rd, .V3FYCf');
                    let snippet = snippetEl ? snippetEl.innerText.trim() : '';
                    if (title && snippet) {
                        results.push({ Title: title, Description: snippet, Url: url });
                    }
                }
                return JSON.stringify(results);
            })();
        ";

        /// <summary>
        /// JavaScript that extracts search results from Bing.
        /// </summary>
        private const string BingJsExtractor = @"
            (() => {
                let results = [];
                let nodes = document.querySelectorAll('li.b_algo');
                for (let i = 0; i < Math.min(nodes.length, 6); i++) {
                    let titleEl = nodes[i].querySelector('h2 a');
                    let title = titleEl ? titleEl.innerText.trim() : '';
                    let url = titleEl ? titleEl.href : '';
                    let snippetEl = nodes[i].querySelector('.b_caption p, .b_lineclamp3, .b_snippet');
                    let snippet = snippetEl ? snippetEl.innerText.trim() : '';
                    if (title && snippet && url) {
                        results.push({ Title: title, Description: snippet, Url: url });
                    }
                }
                return JSON.stringify(results);
            })();
        ";

        /// <summary>
        /// JavaScript that extracts search results from DuckDuckGo HTML version.
        /// This is the most reliable scraper for headless mode.
        /// </summary>
        private const string DdgJsExtractor = @"
            (() => {
                let results = [];
                let nodes = document.querySelectorAll('.result');
                for (let i = 0; i < Math.min(nodes.length, 8); i++) {
                    let title = nodes[i].querySelector('.result__title')?.innerText?.trim() || '';
                    let snippet = nodes[i].querySelector('.result__snippet')?.innerText?.trim() || '';
                    let url = nodes[i].querySelector('.result__url')?.href || nodes[i].querySelector('a.result__a')?.href || '';
                    if (title && snippet) {
                        results.push({ Title: title, Description: snippet, Url: url });
                    }
                }
                return JSON.stringify(results);
            })();
        ";

        /// <summary>
        /// Navigates to a URL and waits for the page to finish loading.
        /// This is the "patient waiter" that makes sure the page is ready before scraping.
        /// </summary>
        private async Task NavigateAndWaitAsync(WebView2 webView, string url)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
            handler = (sender, e) =>
            {
                webView.CoreWebView2.NavigationCompleted -= handler;
                if (e.IsSuccess)
                    tcs.TrySetResult(true);
                else
                    tcs.TrySetException(new Exception($"Navigation failed: {e.WebErrorStatus}"));
            };

            webView.CoreWebView2.NavigationCompleted += handler;
            webView.CoreWebView2.Navigate(url);

            await tcs.Task;
        }

        /// <summary>
        /// Runs the JavaScript scraper and returns clean search results.
        /// If JavaScript fails, returns an empty list instead of crashing.
        /// </summary>
        private async Task<List<SearchResult>> ExtractResultsAsync(WebView2 webView, string jsExtractor)
        {
            try
            {
                string jsonResult = await webView.CoreWebView2.ExecuteScriptAsync(jsExtractor);
                string unescapedJson = JsonSerializer.Deserialize<string>(jsonResult) ?? "[]";
                return JsonSerializer.Deserialize<List<SearchResult>>(unescapedJson) ?? new List<SearchResult>();
            }
            catch
            {
                return new List<SearchResult>(); // Graceful failure
            }
        }

        /// <summary>
        /// The heart of the tool. Runs the invisible browser, visits search engines, scrapes results,
        /// and returns a nicely formatted summary for the AI.
        /// </summary>
        private async void RunHeadlessBrowser(string query, string provider, bool combined, TaskCompletionSource<string> tcs)
        {
            Form? hiddenForm = null;
            WebView2? webView = null;

            try
            {
                // Create invisible window (no one will see it)
                hiddenForm = new Form
                {
                    Width = 0,
                    Height = 0,
                    ShowInTaskbar = false,
                    Visible = false,
                    WindowState = FormWindowState.Minimized,
                    FormBorderStyle = FormBorderStyle.None,
                    Opacity = 0
                };

                webView = new WebView2 { Dock = DockStyle.Fill };
                hiddenForm.Controls.Add(webView);

                // Force handle creation (required by WebView2)
                var handle = hiddenForm.Handle;

                // Configure browser with quiet settings to reduce console spam
                var envOptions = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments =
                        "--user-agent=\"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36\" " +
                        "--headless " +
                        "--no-sandbox " +
                        "--disable-gpu " +
                        "--disable-features=OptimizationHints,SmartScreen,EdgeEnhancement,AutofillServerCommunication"
                };

                var environment = await CoreWebView2Environment.CreateAsync(null, null, envOptions);
                await webView.EnsureCoreWebView2Async(environment);

                // Decide which engines to search
                List<ProviderConfig> providersToSearch = combined
                    ? new List<ProviderConfig> { AvailableProviders["DuckDuckGo"], AvailableProviders["Google"], AvailableProviders["Bing"] }
                    : new List<ProviderConfig> { AvailableProviders[provider] };

                var collectedResults = new Dictionary<string, List<SearchResult>>();

                foreach (var config in providersToSearch)
                {
                    string searchUrl = string.Format(config.SearchUrlTemplate, Uri.EscapeDataString(query));

                    try
                    {
                        await NavigateAndWaitAsync(webView, searchUrl);
                        var results = await ExtractResultsAsync(webView, config.JsExtractor);
                        collectedResults[config.Name] = results;
                    }
                    catch
                    {
                        collectedResults[config.Name] = new List<SearchResult>();
                    }
                }

                // Build beautiful output for the AI
                var sb = new StringBuilder();
                string header = combined
                    ? $"--- 🌟 Combined Search Results from DuckDuckGo, Google & Bing for: {query} ---"
                    : $"--- 🔎 Search Results from {providersToSearch[0].Name} for: {query} ---";

                sb.AppendLine(header);

                bool hasAnyResults = false;

                foreach (var kvp in collectedResults)
                {
                    sb.AppendLine($"\n📌 From {kvp.Key}:");

                    if (kvp.Value.Count == 0)
                    {
                        sb.AppendLine("  (No good results found this time — the engine was being shy!)");
                    }
                    else
                    {
                        hasAnyResults = true;
                        foreach (var res in kvp.Value)
                        {
                            sb.AppendLine($"Title: {res.Title}");
                            sb.AppendLine($"URL: {res.Url}");
                            sb.AppendLine($"Snippet: {res.Description}");
                            sb.AppendLine();
                        }
                    }
                }

                string finalOutput = hasAnyResults
                    ? sb.ToString()
                    : $"[SORRY] No useful search results found for: {query}";

                tcs.TrySetResult(finalOutput);
            }
            catch (Exception ex)
            {
                tcs.TrySetResult($"[OOPS] Invisible browser had a problem: {ex.Message}");
            }
            finally
            {
                // Clean up resources
                hiddenForm?.Dispose();
                webView?.Dispose();
                Application.ExitThread();
            }
        }

        // ====================================================================
        // SIMPLE DATA CLASSES
        // ====================================================================

        private class SearchArgs
        {
            [JsonPropertyName("query")] public string Query { get; set; } = "";
            [JsonPropertyName("provider")] public string Provider { get; set; } = "Google";
            [JsonPropertyName("combined")] public bool Combined { get; set; } = true;
        }

        private class SearchResult
        {
            public string Title { get; set; } = "";
            public string Url { get; set; } = "";
            public string Description { get; set; } = "";
        }
    }
}