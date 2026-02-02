using RustCardTimers.Data;

namespace RustCardTimers.Services;

public sealed class ResetSchedulerService : BackgroundService
{
    private readonly MonumentRepository _repo;
    private readonly ILogger<ResetSchedulerService> _log;

    private static readonly TimeZoneInfo NyTz =
        TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    public ResetSchedulerService(MonumentRepository repo, ILogger<ResetSchedulerService> log)
    {
        _repo = repo;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var nowUtc = DateTime.UtcNow;
            var nextUtc = GetNextResetUtc(nowUtc);

            var delay = nextUtc - nowUtc;
            if (delay < TimeSpan.Zero) delay = TimeSpan.FromSeconds(1);

            _log.LogInformation("Next reset scheduled at {NextUtc} (in {Delay}).", nextUtc.ToString("O"), delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            var resetUtc = DateTime.UtcNow;
            _repo.SetLastResetUtc(resetUtc);
            _log.LogInformation("Reset epoch updated: {ResetUtc}", resetUtc.ToString("O"));
        }
    }

    private static DateTime GetNextResetUtc(DateTime nowUtc)
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, NyTz);

        // Today at 3:00 and 15:00 local
        var today3 = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, 3, 0, 0, DateTimeKind.Unspecified);
        var today15 = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, 15, 0, 0, DateTimeKind.Unspecified);

        DateTime nextLocal =
            nowLocal < today3 ? today3 :
            nowLocal < today15 ? today15 :
            // otherwise tomorrow 3:00
            today3.AddDays(1);

        return TimeZoneInfo.ConvertTimeToUtc(nextLocal, NyTz);
    }
}
