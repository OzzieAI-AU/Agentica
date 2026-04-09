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
    /// Imagine you have a super-smart invisible robot that can open a real web browser,
    /// visit Google, Bing, or DuckDuckGo, and bring back the best answers — without
    /// any websites knowing it's a robot! 
    /// 
    /// This tool now has THREE search engines:
    ///   • Google (the default hero)
    ///   • Bing
    ///   • DuckDuckGo (the privacy-friendly one)
    /// 
    /// You can ask it to:
    ///   • Search just ONE engine (set combined = false)
    ///   • Or search ALL THREE and combine the results into one beautiful list (default!)
    /// 
    /// Everything is explained below like a story so even a curious 10-year-old coder
    /// can understand why each line exists. Ready? Let's make some search magic!
    /// </summary>
    public class WebSearchTool : IAgentTool
    {
        /// <summary>
        /// The name the AI agent will call when it wants to search the web.
        /// </summary>
        public string Name => "web_search";

        /// <summary>
        /// A friendly description that tells the AI what this tool can do.
        /// </summary>
        public string Description =>
            "Performs a deep-web search using a real invisible browser (WebView2) to bypass bot detection. " +
            "Supports Google, Bing, and DuckDuckGo. " +
            "Can combine results from all three engines for super-powerful answers!";

        /// <summary>
        /// Tells the AI exactly what information it needs to give us (like a recipe).
        /// Now includes "provider" and "combined" options!
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
        /// This is where the AI calls us with JSON. We read the instructions and start the magic.
        /// </summary>
        public Task<string> ExecuteAsync(string jsonArguments)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var args = JsonSerializer.Deserialize<SearchArgs>(jsonArguments, options);

            // Safety check — never search with nothing!
            if (string.IsNullOrWhiteSpace(args?.Query))
                return Task.FromResult("Error: Empty query. Please tell me what you want to search for!");

            // Friendly defaults (just like the user asked)
            string provider = string.IsNullOrWhiteSpace(args.Provider) ? "Google" : args.Provider.Trim();
            if (!AvailableProviders.ContainsKey(provider))
                provider = "Google"; // always fall back safely

            bool combined = args.Combined; // defaults to true in the class below

            // Because WebView2 needs a special "STA" thread and a message pump (like a heartbeat),
            // we use this trick to run everything safely in the background.
            var tcs = new TaskCompletionSource<string>();

            var thread = new Thread(() =>
            {
                try
                {
                    RunHeadlessBrowser(args.Query, provider, combined, tcs);
                    // This starts the invisible window's "heartbeat" so the browser can work.
                    Application.Run();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                    Application.ExitThread();
                }
            });

            // CRITICAL: WebView2 is picky — it MUST run in a Single-Threaded Apartment (STA).
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true; // so it doesn't keep the whole program alive forever
            thread.Start();

            return tcs.Task;
        }

        // ====================================================================
        // PRIVATE HELPER CLASSES & CONSTANTS (the secret ingredients)
        // ====================================================================

        /// <summary>
        /// Simple container that holds everything we need for one search engine.
        /// Think of it as a little recipe card for each website.
        /// </summary>
        private sealed class ProviderConfig
        {
            public string Name { get; set; } = string.Empty;
            public string SearchUrlTemplate { get; set; } = string.Empty;
            public string JsExtractor { get; set; } = string.Empty;
        }

        /// <summary>
        /// The three magical search engines and exactly how to talk to each one.
        /// Order is Google → Bing → DuckDuckGo when combining (nice and predictable).
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
                    // "https://html.duckduckgo.com/html/?q={0}",
                    SearchUrlTemplate = "https://html.duckduckgo.com/html/?q={0}",
                    JsExtractor = DdgJsExtractor
                }
            }
        };

        // JavaScript "extractors" — these are like little robots that read the webpage
        // and pull out the titles, links, and snippets. Each engine has its own style.
        // === IMPROVED EXTRACTORS ===
        // Use DuckDuckGo HTML as primary (most stable for headless scraping)
        private const string GoogleJsExtractor = @"
            (() => {
                let results = [];
                let nodes = document.querySelectorAll('div.g');
                for (let i = 0; i < Math.min(nodes.length, 6); i++) {
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

        private const string BingJsExtractor = @"
            (() => {
                let results = [];
                let nodes = document.querySelectorAll('li.b_algo');
                for(let i = 0; i < Math.min(nodes.length, 5); i++) {
                    let titleEl = nodes[i].querySelector('h2 a');
                    let title = titleEl ? titleEl.innerText.trim() : '';
                    let url = titleEl ? titleEl.href : '';
                    let snippetEl = nodes[i].querySelector('.b_caption p') || nodes[i].querySelector('.b_lineclamp3') || nodes[i].querySelector('.b_snippet');
                    let snippet = snippetEl ? snippetEl.innerText.trim() : '';
                    if(title && snippet && url) {
                        results.push({ Title: title, Description: snippet, Url: url });
                    }
                }
                return JSON.stringify(results);
            })();
        ";

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

        // Fallback Google extractor (more resilient)

        /// <summary>
        /// Waits for a page to finish loading. This is the "patient waiter" helper.
        /// </summary>
        private async Task NavigateAndWaitAsync(WebView2 webView, string url)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
            handler = (sender, e) =>
            {
                // Clean up after ourselves so we don't get memory leaks
                webView.CoreWebView2.NavigationCompleted -= handler;
                if (e.IsSuccess)
                    tcs.TrySetResult(true);
                else
                    tcs.TrySetException(new Exception($"Navigation failed with error code: {e.WebErrorStatus}"));
            };

            webView.CoreWebView2.NavigationCompleted += handler;
            webView.CoreWebView2.Navigate(url);

            await tcs.Task;
        }

        /// <summary>
        /// Runs the JavaScript robot that scrapes the results from the page.
        /// Returns up to 5 clean results per engine.
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
                // If JavaScript fails for any reason, just return empty list (we'll show a nice message)
                return new List<SearchResult>();
            }
        }

        /// <summary>
        /// The heart of the magic! Runs on a secret background thread and does ALL the searching.
        /// Now handles one engine OR all three, exactly as you asked.
        /// </summary>
        private async void RunHeadlessBrowser(string query, string provider, bool combined, TaskCompletionSource<string> tcs)
        {
            Form? hiddenForm = null;
            WebView2? webView = null;

            try
            {
                // Step 1: Create an invisible window (no one will see it!)
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

                // Step 2: Put the invisible browser inside the invisible window
                webView = new WebView2 { Dock = DockStyle.Fill };
                hiddenForm.Controls.Add(webView);

                // Force the window to exist (WebView2 needs this)
                var handle = hiddenForm.Handle;

                // Step 3: Tell the browser to act like a normal Chrome (with a cool user-agent)
                var envOptions = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments =
                    "--user-agent=\"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36\" " +
                    "--headless " +
                    "--disable-features=OptimizationHints,SmartScreen,EdgeEnhancement " +
                    "--no-sandbox"
                };
                var environment = await CoreWebView2Environment.CreateAsync(null, null, envOptions);

                await webView.EnsureCoreWebView2Async(environment);

                // Step 4: Decide which engines to visit today
                List<ProviderConfig> providersToSearch;
                if (combined)
                {
                    // ALL THREE! In nice order: Google first, then Bing, then DuckDuckGo
                    providersToSearch = new List<ProviderConfig>
                    {
                        AvailableProviders["Google"],
                        AvailableProviders["Bing"],
                        AvailableProviders["DuckDuckGo"]
                    };
                }
                else
                {
                    // Just the one the user (or default) asked for
                    providersToSearch = new List<ProviderConfig> { AvailableProviders[provider] };
                }

                // Step 5: Go visit each engine one by one and collect the treasures
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
                        // If one engine fails, we still show the others
                        collectedResults[config.Name] = new List<SearchResult>();
                    }
                }

                // Step 6: Turn all the treasures into a beautiful message for the AI
                var sb = new StringBuilder();
                string header = combined
                    ? $"--- 🌟 Combined Search Results from Google, Bing & DuckDuckGo for: {query} ---"
                    : $"--- 🔎 Search Results from {providersToSearch[0].Name} for: {query} ---";

                sb.AppendLine(header);

                bool hasAnyResults = false;

                foreach (var kvp in collectedResults)
                {
                    sb.AppendLine($"\n📌 From {kvp.Key}:");

                    if (kvp.Value.Count == 0)
                    {
                        sb.AppendLine("  (No results found this time — the engine was being shy!)");
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
                    : $"[SORRY] No search results found from any provider for: {query}";

                tcs.TrySetResult(finalOutput);
            }
            catch (Exception ex)
            {
                tcs.TrySetResult($"[OOPS] The invisible browser had a tiny hiccup: {ex.Message}");
            }
            finally
            {
                // Clean up our invisible friends so they don't hang around
                hiddenForm?.Dispose();
                webView?.Dispose();
                Application.ExitThread();
            }
        }

        // ====================================================================
        // DATA CLASSES (simple boxes to hold information)
        // ====================================================================

        private class SearchArgs
        {
            [JsonPropertyName("query")]
            public string Query { get; set; } = "";

            [JsonPropertyName("provider")]
            public string Provider { get; set; } = "Google"; // default as requested

            [JsonPropertyName("combined")]
            public bool Combined { get; set; } = true; // flag is true by default!
        }

        private class SearchResult
        {
            public string Title { get; set; } = "";
            public string Url { get; set; } = "";
            public string Description { get; set; } = "";
        }
    }
}