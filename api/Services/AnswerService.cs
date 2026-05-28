using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AvatarDocReader.Api.Models;
using AvatarDocReader.Api.Options;

namespace AvatarDocReader.Api.Services;

public sealed class AnswerService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;

    public AnswerService(IConfiguration config, IHttpClientFactory httpFactory)
    {
        _config = config;
        _httpFactory = httpFactory;
    }

    public async Task<string> AnswerAsync(
        string message,
        IReadOnlyList<KnowledgeItem> matches,
        bool sendFilesToModel,
        CancellationToken ct)
    {
        var openAi = _config.GetSection("OpenAI").Get<OpenAiOptions>() ?? new();
        var modelFiles = sendFilesToModel
            ? matches.Where(item => item.CanSendToModel).Take(8).ToArray()
            : [];

        if (sendFilesToModel && modelFiles.Length > 0 && openAi.IsConfigured)
            return await AnswerWithOpenAiResponsesAsync(message, matches, modelFiles, openAi, ct);

        var azureOpenAi = _config.GetSection("AzureOpenAI").Get<AzureOpenAiOptions>() ?? new();
        if (!azureOpenAi.IsConfigured)
            return BuildLocalAnswer(message, matches, sendFilesToModel, modelFiles.Length, openAi.IsConfigured);

        return await AnswerWithAzureOpenAiAsync(message, matches, azureOpenAi, sendFilesToModel, modelFiles.Length, openAi.IsConfigured, ct);
    }

    private async Task<string> AnswerWithAzureOpenAiAsync(
        string message,
        IReadOnlyList<KnowledgeItem> matches,
        AzureOpenAiOptions azureOpenAi,
        bool sendFilesToModel,
        int modelFileCount,
        bool openAiConfigured,
        CancellationToken ct)
    {
        using var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Add("api-key", azureOpenAi.ApiKey);

        // Only actual images can be sent via image_url — PDFs are covered by their extracted text in BuildContext
        var images = sendFilesToModel
            ? matches.Where(m => m.Kind == "image" && !string.IsNullOrWhiteSpace(m.ModelDataBase64)).Take(4).ToList()
            : [];

        // Build user content — text + inline images when available
        object userContent;
        if (images.Count > 0)
        {
            var parts = new List<object>
            {
                new { type = "text", text = $"Library context:\n{BuildContext(matches)}\n\nUser request:\n{message}" }
            };
            foreach (var img in images)
                parts.Add(new { type = "image_url", image_url = new { url = $"data:{img.ContentType};base64,{img.ModelDataBase64}" } });
            userContent = parts;
        }
        else
        {
            userContent = $"Library context:\n{BuildContext(matches)}\n\nUser request:\n{message}";
        }

        var endpoint = $"{azureOpenAi.Endpoint.TrimEnd('/')}/openai/deployments/{azureOpenAi.Deployment}/chat/completions?api-version={azureOpenAi.ApiVersion}";
        var body = new
        {
            messages = new object[]
            {
                new { role = "system", content = "You are a professional document assistant avatar that speaks answers aloud. Your sole purpose is to help users understand and query the documents in their library. Answer using the provided library context and any images or documents supplied. Write in plain spoken sentences only — no markdown, no bullet points, no hashtags, no asterisks, no headers. Speak naturally as if talking to someone face to face. Be concise and clear. If the user asks anything unrelated to the uploaded documents — such as general knowledge, personal questions, harmful, offensive, sexual, political, or inappropriate content — politely decline and remind them that you are only here to help with the uploaded documents." },
                new { role = "user", content = userContent }
            },
            temperature = 0.3,
            max_tokens = 700
        };

        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await client.PostAsync(endpoint, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            return BuildLocalAnswer(message, matches, sendFilesToModel, modelFileCount, openAiConfigured) +
                   $"\n\nNote: Azure OpenAI request failed: {err}";
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? BuildLocalAnswer(message, matches, sendFilesToModel, modelFileCount, openAiConfigured);
    }

    private async Task<string> AnswerWithOpenAiResponsesAsync(
        string message,
        IReadOnlyList<KnowledgeItem> matches,
        IReadOnlyList<KnowledgeItem> modelFiles,
        OpenAiOptions openAi,
        CancellationToken ct)
    {
        using var client = _httpFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openAi.ApiKey);

        var contentParts = new List<object>
        {
            new
            {
                type = "input_text",
                text = "You are a professional document assistant avatar that speaks answers aloud. Your sole purpose is to help users understand and query the documents in their library. Read the attached images and PDFs directly and use text snippets as extra context. Write in plain spoken sentences only — no markdown, no bullet points, no hashtags, no asterisks, no headers. Speak naturally as if talking to someone face to face. If the user asks anything unrelated to the uploaded documents — such as general knowledge, personal questions, harmful, offensive, sexual, political, or inappropriate content — politely decline and remind them that you are only here to help with the uploaded documents.\n\n" +
                       $"Text/index context:\n{BuildContext(matches)}\n\nUser request:\n{message}"
            }
        };

        foreach (var file in modelFiles)
        {
            if (file.Kind == "image")
            {
                contentParts.Add(new
                {
                    type = "input_image",
                    image_url = $"data:{file.ContentType};base64,{file.ModelDataBase64}"
                });
            }
            else if (file.Kind == "pdf")
            {
                contentParts.Add(new
                {
                    type = "input_file",
                    filename = file.Name,
                    file_data = $"data:application/pdf;base64,{file.ModelDataBase64}"
                });
            }
        }

        var body = new
        {
            model = openAi.Model,
            input = new[]
            {
                new
                {
                    role = "user",
                    content = contentParts
                }
            },
            temperature = 0.25,
            max_output_tokens = 900
        };

        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await client.PostAsync("https://api.openai.com/v1/responses", content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            return BuildLocalAnswer(message, matches, true, modelFiles.Count, openAi.IsConfigured) +
                   $"\n\nNote: OpenAI file/vision request failed: {error}";
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (doc.RootElement.TryGetProperty("output_text", out var outputText))
            return outputText.GetString() ?? "";

        if (doc.RootElement.TryGetProperty("output", out var output))
        {
            var builder = new StringBuilder();
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var contentArray)) continue;
                foreach (var part in contentArray.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text))
                        builder.Append(text.GetString());
                }
            }

            if (builder.Length > 0) return builder.ToString();
        }

        return "The model responded, but I could not parse the text output.";
    }

    private static string BuildContext(IReadOnlyList<KnowledgeItem> matches)
    {
        if (matches.Count == 0) return "No uploaded library content yet.";

        var builder = new StringBuilder();
        foreach (var item in matches)
        {
            builder.AppendLine($"Source: {item.Name} ({item.Kind})");
            builder.AppendLine(string.IsNullOrWhiteSpace(item.Text)
                ? item.Description
                : item.Text[..Math.Min(item.Text.Length, 2400)]);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildLocalAnswer(
        string message,
        IReadOnlyList<KnowledgeItem> matches,
        bool sendFilesToModel = false,
        int modelFileCount = 0,
        bool openAiConfigured = false)
    {
        if (matches.Count == 0)
            return "Upload files, folders, images, audio, or videos first. I can then read text files directly and keep media metadata ready for OCR, transcription, or visual analysis extensions.";

        var builder = new StringBuilder();
        if (sendFilesToModel && modelFileCount > 0 && !openAiConfigured)
        {
            builder.AppendLine($"You asked to send PDFs/images to the model, and I found {modelFileCount} model-readable file(s), but OpenAI is not configured yet. Add the OpenAI section in appsettings to let the model view PDFs/images directly.");
            builder.AppendLine();
        }

        builder.AppendLine("I found relevant material in the uploaded library. Here are the strongest matches:");
        foreach (var item in matches.Take(4))
        {
            builder.AppendLine();
            builder.AppendLine($"- {item.Name}: {item.Description}");
            if (!string.IsNullOrWhiteSpace(item.Text))
            {
                var snippet = item.Text.ReplaceLineEndings(" ");
                builder.AppendLine($"  {snippet[..Math.Min(snippet.Length, 260)]}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Configure AzureOpenAI in appsettings to get full natural-language answers grounded in these sources.");
        return builder.ToString();
    }
}
