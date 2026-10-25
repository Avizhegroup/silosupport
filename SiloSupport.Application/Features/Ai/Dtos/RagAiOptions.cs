namespace SiloSupport.Application;

/// <summary>Configuration options for calling the Silo AI RAG chat API.</summary>
public class RagAiOptions
{
    public const string SectionName = "SiloAI";

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public int TopK { get; set; } = 5;

    public bool IsMainChat { get; set; }

    public RagDocType DocType { get; set; } = RagDocType.GeneralChat;

    public string? Key { get; set; }
}
