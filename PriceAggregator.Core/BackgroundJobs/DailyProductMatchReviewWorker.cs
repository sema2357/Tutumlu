using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PriceAggregator.Core.Matching;

namespace PriceAggregator.Core.BackgroundJobs;

public class DailyProductMatchReviewWorker : BackgroundService
{
    private readonly string _connectionString;
    private readonly ILogger<DailyProductMatchReviewWorker> _logger;

    public DailyProductMatchReviewWorker(IConfiguration config, ILogger<DailyProductMatchReviewWorker> logger)
    {
        _connectionString = config.GetConnectionString("DefaultConnection")!;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextRun = now.Date.AddDays(1).AddMinutes(15); // Her gece 00:15
            var delay = nextRun - now;

            _logger.LogInformation("[ProductMatchReview] Bir sonraki tarama zamanı: {NextRun}", nextRun);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            try
            {
                using var db = new SqlConnection(_connectionString);
                var suspects = await ProductMatchReviewer.FindSuspectsAsync(db);

                if (suspects.Count > 0)
                {
                    _logger.LogWarning(
                        "[ProductMatchReview] {Count} şüpheli ürün eşleşmesi bulundu.",
                        suspects.Count);
                }
                else
                {
                    _logger.LogInformation("[ProductMatchReview] Şüpheli eşleşme bulunamadı.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ProductMatchReview] Tarama sırasında hata oluştu.");
            }
        }
    }
}
