using Hangfire;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Services.Implementations;

/// <summary>
/// 復旧実行をHangfireの"recovery"キューへ積む。実際の実行はWorkerプロセスが行う。
/// APIプロセスはHangfireクライアントとしてのみ動作し、ジョブサーバーは起動しない。
/// </summary>
public class HangfireRecoveryJobQueue(IBackgroundJobClient jobClient) : IRecoveryJobQueue
{
    public void Enqueue(long recoveryActionId) =>
        jobClient.Create<IRecoveryExecutionService>(
            service => service.ExecuteAsync(recoveryActionId, CancellationToken.None),
            new Hangfire.States.EnqueuedState("recovery"));
}

/// <summary>
/// Hangfireが未設定の環境(接続文字列なし・テスト等)で使うフォールバック。
/// 実行は行わず、キューに積めなかったことをログへ残す。
/// </summary>
public class NoopRecoveryJobQueue(ILogger<NoopRecoveryJobQueue> logger) : IRecoveryJobQueue
{
    public void Enqueue(long recoveryActionId) =>
        logger.LogWarning(
            "Recovery job queue is not configured; action {ActionId} was not dispatched to a worker.",
            recoveryActionId);
}
