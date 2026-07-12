using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ServerOperations.Core.Data;

/// <summary>
/// dotnet ef コマンド(マイグレーション作成・スクリプト生成)用のファクトリ。
/// 実DBへは接続しないため、接続文字列はダミーで良い。
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(
                "Server=localhost;Database=design_time_only;User=design;Password=design;",
                new MySqlServerVersion(new Version(8, 4, 0)))
            .Options;

        return new AppDbContext(options);
    }
}
