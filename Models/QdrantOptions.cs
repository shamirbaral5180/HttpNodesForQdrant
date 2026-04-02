namespace HttpNodesForQdrant.Models;

public sealed class QdrantOptions
{
    public required string BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public required string CollectionName { get; set; }
    public int VectorSize { get; set; } = 128;
    public string Distance { get; set; } = "Cosine";
}
