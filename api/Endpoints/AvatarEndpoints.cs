using System.Text;
using AvatarDocReader.Api.Options;

namespace AvatarDocReader.Api.Endpoints;

public static class AvatarEndpoints
{
    public static IEndpointRouteBuilder MapAvatarEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/avatar/token", async (
            string? avatarCharacter,
            string? avatarStyle,
            IConfiguration config,
            IHttpClientFactory httpFactory,
            CancellationToken ct) =>
        {
            var speech = config.GetSection("AzureSpeech").Get<AzureSpeechOptions>() ?? new();
            if (string.IsNullOrWhiteSpace(speech.SubscriptionKey) || string.IsNullOrWhiteSpace(speech.Region))
                return Results.BadRequest("Set AzureSpeech:SubscriptionKey and AzureSpeech:Region.");

            var character = string.IsNullOrWhiteSpace(avatarCharacter) ? speech.AvatarCharacter : avatarCharacter.Trim();
            var style = string.IsNullOrWhiteSpace(avatarStyle) ? speech.AvatarStyle : avatarStyle.Trim();

            if (!AvatarOptions.Catalog.TryGetValue(character, out var styles) || !styles.Contains(style))
                return Results.BadRequest("Invalid Azure Talking Avatar character/style selection.");

            using var client = httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", speech.SubscriptionKey);

            var endpoint = $"https://{speech.Region}.api.cognitive.microsoft.com/sts/v1.0/issueToken";
            var response = await client.PostAsync(endpoint, new StringContent(string.Empty, Encoding.UTF8), ct);
            if (!response.IsSuccessStatusCode)
                return Results.StatusCode(502);

            return Results.Ok(new
            {
                token = await response.Content.ReadAsStringAsync(ct),
                region = speech.Region,
                avatarCharacter = character,
                avatarStyle = style
            });
        });

        app.MapGet("/api/avatar/relay-token", async (
            IConfiguration config,
            IHttpClientFactory httpFactory,
            CancellationToken ct) =>
        {
            var speech = config.GetSection("AzureSpeech").Get<AzureSpeechOptions>() ?? new();
            if (string.IsNullOrWhiteSpace(speech.SubscriptionKey) || string.IsNullOrWhiteSpace(speech.Region))
                return Results.BadRequest("Set AzureSpeech:SubscriptionKey and AzureSpeech:Region.");

            using var client = httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", speech.SubscriptionKey);

            var endpoint = $"https://{speech.Region}.tts.speech.microsoft.com/cognitiveservices/avatar/relay/token/v1";
            var response = await client.GetAsync(endpoint, ct);
            if (!response.IsSuccessStatusCode)
                return Results.StatusCode(502);

            var json = await response.Content.ReadAsStringAsync(ct);
            return Results.Content(json, "application/json");
        });

        return app;
    }
}
