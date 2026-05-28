using AvatarDocReader.Api.Models;

namespace AvatarDocReader.Api.Services;

public sealed class KnowledgeStore
{
    private readonly object _gate = new();
    private readonly List<KnowledgeItem> _items = new();

    public IReadOnlyList<KnowledgeItemSummary> Items
    {
        get
        {
            lock (_gate) return _items.Select(KnowledgeItemSummary.From).ToArray();
        }
    }

    public object Stats
    {
        get
        {
            lock (_gate)
            {
                return new
                {
                    total = _items.Count,
                    text = _items.Count(i => i.Kind == "text"),
                    images = _items.Count(i => i.Kind == "image"),
                    pdfs = _items.Count(i => i.Kind == "pdf"),
                    audio = _items.Count(i => i.Kind == "audio"),
                    videos = _items.Count(i => i.Kind == "video"),
                    other = _items.Count(i => i.Kind == "other")
                };
            }
        }
    }

    public void Upsert(KnowledgeItem item)
    {
        lock (_gate)
        {
            _items.RemoveAll(i => string.Equals(i.Path, item.Path, StringComparison.OrdinalIgnoreCase));
            _items.Add(item);
        }
    }

    public IReadOnlyList<KnowledgeItem> Search(string query, int take)
    {
        var terms = query
            .Split([' ', '\t', '\r', '\n', '.', ',', '?', '!', ':', ';', '/', '\\', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 2)
            .Select(t => t.ToLowerInvariant())
            .Distinct()
            .ToArray();

        lock (_gate)
        {
            var scored = _items
                .Select(item => new
                {
                    Item = item,
                    Score = terms.Sum(term =>
                        Count(item.Name, term) * 4 +
                        Count(item.Path, term) * 3 +
                        Count(item.Description, term) * 2 +
                        Count(item.Text, term))
                })
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Item.Name)
                .ToArray();

            // Always include visual/binary items (images, PDFs) — they have no text to keyword-match
            var visual = scored.Where(x => x.Item.CanSendToModel).Select(x => x.Item).ToArray();
            var textMatches = scored.Where(x => x.Score > 0 || terms.Length == 0).Select(x => x.Item).ToArray();

            return visual.Union(textMatches).Take(take).ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate) _items.Clear();
    }

    private static int Count(string? source, string term)
    {
        if (string.IsNullOrWhiteSpace(source)) return 0;

        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += term.Length;
        }

        return count;
    }
}
