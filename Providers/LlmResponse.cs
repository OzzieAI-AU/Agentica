namespace OzzieAI.Agentica.Providers
{
    public class LlmResponse : IChatResponse
    {
        public string Content { get; set; } = string.Empty;
        public List<ToolCall>? ToolCalls { get; set; }
    }
}