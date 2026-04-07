namespace OzzieAI.Agentica.Providers
{
    using System;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Native LLM Caller using standard HttpClient. No 3rd party SDKs.
    /// </summary>
    public class NativeLlmClient
    {


        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _endpoint;



        public NativeLlmClient(string endpoint, string apiKey)
        {
        
            _httpClient = new HttpClient();
            _endpoint = endpoint;
            _apiKey = apiKey;
        }



        public async Task<string> AskLlmAsync(string prompt, CancellationToken cancellationToken = default)
        {

            // Fallback for simulation if no API key is provided
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                await Task.Delay(500, cancellationToken); // Simulate network latency
                return $"[LLM Simulated Response] Processed: {prompt}";
            }

            var payload = new
            {
                model = "gpt-4", // Or whichever model you are routing to
                messages = new[] { new { role = "user", content = prompt } }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

            try
            {
                var response = await _httpClient.PostAsync(_endpoint, content, cancellationToken);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(cancellationToken);

                // Extremely simplified extraction; in production, use JsonDocument to parse choices[0].message.content
                return json;
            }
            catch (Exception ex)
            {
                return $"LLM Calling Error: {ex.Message}";
            }
        }
    }
}