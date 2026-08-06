using Hangfire;
using ServerOperations.Core.Services;

namespace ServerOperations.Worker.Jobs;

/// <summary>
/// 収集をHangfireから呼ぶためのラッパー。
///
/// **キューの指定は属性で行う。**`AddOrUpdate` にキュー名を渡す形は
/// Hangfire.MySqlStorage が対応しておらず、起動時に例外になる
/// (Current storage doesn't support specifying queues directly for a specific job)。
///
/// 属性は呼ばれるメソッドに付ける必要がある。
/// ITargetCollectionService は Core にあるため、そこへHangfireを持ち込まないよう
/// Worker側にこの薄い層を置く。
/// </summary>
[Queue("collection")]
public class CollectionJob(ITargetCollectionService collectionService)
{
    public Task RunAsync(long targetId, CancellationToken ct = default) =>
        collectionService.CollectAsync(targetId, ct);
}
