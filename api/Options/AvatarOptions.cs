namespace AvatarDocReader.Api.Options;

public static class AvatarOptions
{
    public static readonly IReadOnlyDictionary<string, HashSet<string>> Catalog =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["lisa"] = new(StringComparer.OrdinalIgnoreCase)
            {
                "casual-sitting",
                "graceful-sitting",
                "graceful-standing",
                "technical-sitting",
                "technical-standing"
            },
            ["anna"] = new(StringComparer.OrdinalIgnoreCase) { "casual-sitting" },
            ["harry"] = new(StringComparer.OrdinalIgnoreCase) { "business", "casual", "youthful" },
            ["jeff"] = new(StringComparer.OrdinalIgnoreCase) { "business", "formal" },
            ["max"] = new(StringComparer.OrdinalIgnoreCase) { "business" },
        };
}
