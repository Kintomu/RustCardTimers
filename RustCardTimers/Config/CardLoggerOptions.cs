namespace RustCardTimers.Config;

public sealed class CardLoggerOptions
{
    public string DiscordToken { get; set; } = "";
    public ulong ChannelId { get; set; }
    public bool RequireBotAuthor { get; set; } = true;
}