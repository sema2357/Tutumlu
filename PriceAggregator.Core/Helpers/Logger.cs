namespace PriceAggregator.Core.Helpers;

public static class Logger
{
    private static readonly string LogDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

    public static void Log(string message)
    {
        var timestamped = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

        Console.WriteLine(timestamped);

        try
        {
            Directory.CreateDirectory(LogDirectory);
            var fileName = $"scrape-{DateTime.Now:yyyy-MM-dd}.log";
            var filePath = Path.Combine(LogDirectory, fileName);

            File.AppendAllText(filePath, timestamped + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Logger] Failed to write to log file: {ex.Message}");
        }
    }
}