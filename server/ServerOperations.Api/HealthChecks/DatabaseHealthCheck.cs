using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ServerOperations.Core.Data;

namespace ServerOperations.Api.HealthChecks;

/// <summary>
/// DBへ実際に接続できるかを確かめる。readinessにだけ使う。
///
/// 専用のパッケージ(HealthChecks.EntityFrameworkCore)を足さずに済ませている。
/// 確かめたいのは「繋がるか」の1点だけで、そのために依存を1つ増やすと
/// 脆弱性検査の対象がその分広がるため。
/// </summary>
public class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // クエリを投げずに接続だけを確かめる。テーブルの中身に依存しないため、
            // 移行の途中でも「繋がらない」と「データが無い」を取り違えない。
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("データベースへ接続できません。");
        }
        catch (Exception ex)
        {
            // 例外の本文には接続文字列の一部やホスト名が入りうるため、そのまま返さない。
            // 詳細はアプリログ側に出る。
            return HealthCheckResult.Unhealthy("データベースへ接続できません。", ex);
        }
    }
}
