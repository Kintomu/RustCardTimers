using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using RustCardTimers.Config;
using RustCardTimers.Data;

namespace RustCardTimers.Services;

public sealed class DiscordCardLoggerService : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly MonumentRepository _repo;
    private readonly ILogger<DiscordCardLoggerService> _log;
    private readonly CardLoggerOptions _opt;

    // Parse: "... player swiped a card at Monument"
    private static readonly Regex CardRegex = new(
        @"^\s*:desktop:\s*\[Custom\]\s*\[CardLogger\]\s*\[(?<time>\d{1,2}:\d{2}\s[AP]M)\s(?<tz>[A-Z]{2,4})\]\s(?<player>.+?)\sswiped a card at\s(?<monument>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public DiscordCardLoggerService(
        MonumentRepository repo,
        ILogger<DiscordCardLoggerService> log,
        IOptions<CardLoggerOptions> options)
    {
        _repo = repo;
        _log = log;
        _opt = options.Value;

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent
        });

        _client.Log += msg =>
        {
            _log.LogInformation("[Discord] {Message}", msg.ToString());
            return Task.CompletedTask;
        };

        _client.MessageReceived += OnMessageReceivedAsync;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_opt.DiscordToken))
        {
            _log.LogError("DiscordToken is empty. Set CardLogger:DiscordToken in appsettings.");
            return;
        }

        await _client.LoginAsync(TokenType.Bot, _opt.DiscordToken);
        await _client.StartAsync();

        _log.LogInformation("Discord listener started. ChannelId={ChannelId}", _opt.ChannelId);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            // normal shutdown
        }
        finally
        {
            await _client.StopAsync();
        }
    }

    private Task OnMessageReceivedAsync(SocketMessage msg)
    {
        // Only care about the dedicated channel
        if (msg.Channel.Id != _opt.ChannelId) return Task.CompletedTask;

        // Only Pet Rock (bot/app) posts there; keep this guard on.
        if (_opt.RequireBotAuthor && msg.Author is { IsBot: false })
            return Task.CompletedTask;

        var content = msg.Content ?? "";
        var match = CardRegex.Match(content);
        if (!match.Success) return Task.CompletedTask;

        var player = match.Groups["player"].Value.Trim();
        var monument = match.Groups["monument"].Value.Trim();

        // Use Discord timestamp as truth (UTC)
        var eventUtc = msg.Timestamp.UtcDateTime;

        // Rule A: overwrite last swipe
        _repo.UpsertLastSwipe(monument, eventUtc, player, msg.Id.ToString());

        _log.LogInformation("Swipe: {Monument} by {Player} at {Utc}", monument, player, eventUtc.ToString("O"));
        return Task.CompletedTask;
    }
}
