namespace AvatarDocReader.Api.Options;

public sealed class AzureSpeechOptions
{
    public string SubscriptionKey { get; set; } = "";
    public string Region { get; set; } = "";
    public string AvatarCharacter { get; set; } = "lisa";
    public string AvatarStyle { get; set; } = "casual-sitting";
}
