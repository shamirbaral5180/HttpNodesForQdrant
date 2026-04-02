namespace HttpNodesForQdrant.Models;

public sealed class StoreConversationRequest
{
    public required string UserPrompt { get; set; }
    public required string AgentResponse { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public required string WhId { get; set; }
}

public sealed class SearchConversationRequest
{
    public required string UserPrompt { get; set; }
    public string? WhId { get; set; }
    public int Top { get; set; } = 10;
}

public sealed class ConversationSearchResult
{
    public required string UserPrompt { get; set; }
    public required string AgentResponse { get; set; }
    public required DateTimeOffset Timestamp { get; set; }
    public required string WhId { get; set; }
    public double Score { get; set; }
}
