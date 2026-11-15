namespace SiloSupport.Domain.Enums;

/// <summary>The kind of file a customer sent with a Bale message. Stored as its name.</summary>
public enum AttachmentKind
{
    Photo = 0,
    Video = 1,
    Voice = 2,
    Audio = 3,
    Document = 4,
    Animation = 5
}
