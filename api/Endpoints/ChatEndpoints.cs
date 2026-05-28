using AvatarDocReader.Api.Models;
using AvatarDocReader.Api.Services;

namespace AvatarDocReader.Api.Endpoints;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat", async (
            ChatRequest request,
            KnowledgeStore store,
            AnswerService answers,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return Results.BadRequest("Message is required.");

            var matches = store.Search(request.Message, request.SendFilesToModel ? 12 : 8);
            var answer = await answers.AnswerAsync(request.Message, matches, request.SendFilesToModel, ct);

            return Results.Ok(new ChatResponse(
                answer,
                matches.Select(KnowledgeItemSummary.From).ToArray()));
        });

        return app;
    }
}
