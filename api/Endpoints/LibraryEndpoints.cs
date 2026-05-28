using AvatarDocReader.Api.Models;
using AvatarDocReader.Api.Services;

namespace AvatarDocReader.Api.Endpoints;

public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/library", (KnowledgeStore store) => Results.Ok(new
        {
            items = store.Items,
            stats = store.Stats
        }));

        app.MapDelete("/api/library", (KnowledgeStore store) =>
        {
            store.Clear();
            return Results.NoContent();
        });

        app.MapPost("/api/library/files", async (HttpRequest request, KnowledgeStore store, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                return Results.BadRequest("Upload must be multipart/form-data.");

            var form = await request.ReadFormAsync(ct);
            var files = form.Files;
            var paths = form["paths"].ToArray();

            if (files.Count == 0)
                return Results.BadRequest("Upload at least one file.");

            var imported = new List<KnowledgeItem>();
            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                var relativePath = i < paths.Length && !string.IsNullOrWhiteSpace(paths[i])
                    ? paths[i]!
                    : file.FileName;

                var item = await KnowledgeItem.FromFileAsync(file, relativePath, ct);
                store.Upsert(item);
                imported.Add(item);
            }

            return Results.Ok(new
            {
                imported = imported.Select(KnowledgeItemSummary.From),
                stats = store.Stats
            });
        }).DisableAntiforgery();

        return app;
    }
}
