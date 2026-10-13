using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace PriceAggregator.Core.BackgroundJobs;

public class DailySearchSnapshotWorker : BackgroundService
{
    private readonly IConnectionMultiplexer? _redis;
    private readonly string _connectionString;
    private readonly ILogger<DailySearchSnapshotWorker> _logger;

    public DailySearchSnapshotWorker(
        IConfiguration config,
        ILogger<DailySearchSnapshotWorker> logger,
        IConnectionMultiplexer? redis = null)
    {
        _redis = redis;
        _connectionString = config.GetConnectionString("DefaultConnection")!;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextRun = now.Date.AddDays(1).AddMinutes(5); // Her gece 00:05
            var delay = nextRun - now;

            _logger.LogInformation("[DailySearchSnapshot] Bir sonraki aktarım zamanı: {NextRun}", nextRun);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            if (_redis == null || !_redis.IsConnected)
            {
                _logger.LogWarning("[DailySearchSnapshot] Redis bağlantısı bulunamadı, aktarım atlanıyor.");
                continue;
            }

            try
            {
                var yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
                var redisKey = $"popular_searches:{yesterday}";
                var db = _redis.GetDatabase();

                var entries = await db.SortedSetRangeByRankWithScoresAsync(redisKey, 0, -1, Order.Descending);

                if (entries.Length > 0)
                {
                    using var sqlDb = new SqlConnection(_connectionString);
                    foreach (var entry in entries)
                    {
                        var term = entry.Element.ToString();
                        var count = (int)entry.Score;

                        await sqlDb.ExecuteAsync(@"
                            IF NOT EXISTS (SELECT 1 FROM DailySearchTrends WHERE SearchDate = @Date AND SearchTerm = @Term)
                            BEGIN
                                INSERT INTO DailySearchTrends (SearchDate, SearchTerm, SearchCount)
                                VALUES (@Date, @Term, @Count)
                            END", new { Date = yesterday, Term = term, Count = count });
                    }
                    _logger.LogInformation("[DailySearchSnapshot] {Date} tarihine ait {Count} arama terimi SQL'e aktarıldı.", yesterday, entries.Length);
                }
                else
                {
                    _logger.LogInformation("[DailySearchSnapshot] {Date} tarihine ait Redis arama kaydı bulunamadı.", yesterday);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DailySearchSnapshot] Aktarım sırasında hata oluştu.");
            }
        }
    }
}
