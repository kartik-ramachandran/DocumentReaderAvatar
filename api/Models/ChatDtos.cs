namespace AvatarDocReader.Api.Models;

public sealed record ChatRequest(string Message, bool SendFilesToModel = true);

public sealed record ChatResponse(string Answer, IReadOnlyList<KnowledgeItemSummary> Sources);
