namespace AvatarDocReader.Api.Models;

public sealed record KnowledgeItemSummary(
    Guid Id,
    string Name,
    string Path,
    string Kind,
    long Size,
    string Description)
{
    public static KnowledgeItemSummary From(KnowledgeItem item) =>
        new(item.Id, item.Name, item.Path, item.Kind, item.Size, item.Description);
}
