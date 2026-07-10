namespace ServerOperations.Worker;

public class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Worker heartbeat at: {time}", DateTimeOffset.UtcNow);
            }
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }
}
