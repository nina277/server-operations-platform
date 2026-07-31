namespace ServerOperations.Worker;

/// <summary>
/// Workerの生存を示す。
///
/// ログに出すだけでは、コンテナの healthcheck から見えない。
/// 一定間隔でファイルの更新時刻を進め、その古さで生存を判定できるようにする。
/// 追加のパッケージを入れずに済ませるための作りで、HTTPの口は開かない。
///
/// なお「プロセスが生きていること」と「収集が回っていること」は別。
/// 収集が届いているかは、APIが収集の記録から判定して画面へ出す
/// (MonitoringHealthService)。両方が要る。
/// </summary>
public class Worker(ILogger<Worker> logger, IConfiguration configuration) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var heartbeatPath = configuration.GetValue("Worker:HeartbeatPath", "/tmp/worker-alive")!;

        while (!stoppingToken.IsCancellationRequested)
        {
            TouchHeartbeat(heartbeatPath);

            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Worker heartbeat at: {time}", DateTimeOffset.UtcNow);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void TouchHeartbeat(string path)
    {
        try
        {
            // 中身は見ないため空で作る。判定に使うのは更新時刻だけ。
            if (!File.Exists(path))
            {
                File.Create(path).Dispose();
            }

            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            // 書けなくても収集は続ける。健全性の表示が古くなるだけで、
            // ここで落とすと本業まで止まる。
            logger.LogWarning(ex, "Failed to update the heartbeat file at {Path}.", path);
        }
    }
}
