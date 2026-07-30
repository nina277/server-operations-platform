using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.DTOs.Settings;
using ServerOperations.Api.Services.Implementations;
using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

public class IncidentNoteServiceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeIncidentNoteRepository _notes = new();
    private readonly FakeIncidentRepository _incidents = new();
    private readonly FakeAuditService _audit = new();
    private readonly FakeCurrentUserAccessor _currentUser = new();
    private readonly TestTimeProvider _time = new(BaseTime);

    private IncidentNoteService CreateSut() =>
        new(_notes, _incidents, _audit, _currentUser, _time);

    private void SeedIncident(long id = 1) => _incidents.Incidents.Add(new Incident
    {
        Id = id,
        TargetId = 1,
        SignatureSha256 = "sig-a",
        Title = "コンテナが停止しました",
        Classification = "ContainerDown",
        FirstOccurredAt = BaseTime.UtcDateTime,
        LastOccurredAt = BaseTime.UtcDateTime,
    });

    [Fact]
    public async Task メモを追加できる()
    {
        SeedIncident();

        var note = await CreateSut().AddAsync(1, new CreateIncidentNoteRequest
        {
            Body = "ディスクの空きが原因だった。次は df を先に見る。",
        });

        Assert.Equal("ディスクの空きが原因だった。次は df を先に見る。", note.Body);
    }

    [Fact]
    public async Task 書いた人の名前を残す()
    {
        // 誰の判断だったかが分からないと、後から読んでも当てにできない
        SeedIncident();

        var note = await CreateSut().AddAsync(1, new CreateIncidentNoteRequest { Body = "対応済み" });

        Assert.Equal(_currentUser.Username, note.AuthorName);
    }

    [Fact]
    public async Task メモの追加を監査に残す()
    {
        SeedIncident();

        await CreateSut().AddAsync(1, new CreateIncidentNoteRequest { Body = "対応済み" });

        Assert.Contains(_audit.Entries, e => e.Action == "incident.note.add");
    }

    [Fact]
    public async Task 監査にメモの本文は載せない()
    {
        // 本文は運用のメモであり、監査の詳細に複製する必要がない
        SeedIncident();

        await CreateSut().AddAsync(1, new CreateIncidentNoteRequest
        {
            Body = "ここに書いた内容は監査へ複製しない",
        });

        var entry = Assert.Single(_audit.Entries, e => e.Action == "incident.note.add");
        Assert.DoesNotContain("複製しない", entry.Details ?? string.Empty);
    }

    [Fact]
    public async Task 空白だけのメモは拒否する()
    {
        SeedIncident();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().AddAsync(1, new CreateIncidentNoteRequest { Body = "   " }));

        Assert.Equal("note_body_required", ex.Code);
    }

    [Fact]
    public async Task 存在しないインシデントには追加できない()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().AddAsync(999, new CreateIncidentNoteRequest { Body = "対応済み" }));

        Assert.Equal("incident_not_found", ex.Code);
    }

    [Fact]
    public async Task 新しいメモから順に返す()
    {
        SeedIncident();
        var sut = CreateSut();

        await sut.AddAsync(1, new CreateIncidentNoteRequest { Body = "1件目" });
        _time.Advance(TimeSpan.FromMinutes(5));
        await sut.AddAsync(1, new CreateIncidentNoteRequest { Body = "2件目" });

        var notes = await sut.GetForIncidentAsync(1);

        Assert.Equal("2件目", notes[0].Body);
    }

    [Fact]
    public async Task 他のインシデントのメモは返さない()
    {
        SeedIncident(1);
        SeedIncident(2);
        var sut = CreateSut();

        await sut.AddAsync(1, new CreateIncidentNoteRequest { Body = "インシデント1のメモ" });

        var notes = await sut.GetForIncidentAsync(2);

        Assert.Empty(notes);
    }
}

/// <summary>
/// 監査ログのCSV化。外部から与えられた値がそのまま入るため、
/// 表計算ソフトで開いたときに数式として実行されないことを確かめる。
/// </summary>
public class AuditLogCsvWriterTests
{
    private static AuditLogDto Log(
        string userAgent = "Mozilla/5.0",
        string action = "auth.login",
        string? details = null) => new()
    {
        Id = 1,
        OccurredAt = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc),
        ActorUserId = 1,
        ActorName = "admin",
        IpAddress = "192.0.2.10",
        UserAgent = userAgent,
        TargetType = "User",
        TargetId = "1",
        Action = action,
        Result = "Success",
        Details = details,
        TraceId = "trace-1",
    };

    [Fact]
    public void 見出し行を先頭に出す()
    {
        var csv = AuditLogCsvWriter.Write([]);

        Assert.Contains("occurredAt,actorUserId,actorName", csv);
    }

    [Fact]
    public void 監査に必要な項目をすべて出す()
    {
        // 操作者・IP・User-Agent・対象・操作・結果・時刻は必須の記録項目
        var csv = AuditLogCsvWriter.Write([Log()]);

        Assert.Contains("admin", csv);
        Assert.Contains("192.0.2.10", csv);
        Assert.Contains("Mozilla/5.0", csv);
        Assert.Contains("auth.login", csv);
        Assert.Contains("Success", csv);
    }

    [Theory]
    [InlineData("=cmd|'/c calc'!A1")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(A1)")]
    public void 数式として解釈される値を無害化する(string dangerous)
    {
        // User-Agentは外部が自由に決められる。開いた時点で実行されては困る。
        var csv = AuditLogCsvWriter.Write([Log(userAgent: dangerous)]);

        Assert.Contains("\"'" + dangerous, csv);
    }

    [Fact]
    public void 引用符を二重にして壊れないようにする()
    {
        var csv = AuditLogCsvWriter.Write([Log(details: "say \"hello\"")]);

        Assert.Contains("\"say \"\"hello\"\"\"", csv);
    }

    [Fact]
    public void 改行を含む値でも列がずれない()
    {
        var csv = AuditLogCsvWriter.Write([Log(details: "1行目\n2行目")]);

        // 引用で囲まれていれば改行を含んでも1レコードとして読める
        Assert.Contains("\"1行目\n2行目\"", csv);
    }

    [Fact]
    public void 日本語が化けないようBOMを付ける()
    {
        var csv = AuditLogCsvWriter.Write([]);

        Assert.StartsWith("﻿", csv);
    }
}

/// <summary>収集間隔の丸め。指定した通りに動くことを優先する。</summary>
public class CollectionIntervalTests
{
    [Theory]
    [InlineData(1, 60)]
    [InlineData(30, 60)]
    [InlineData(59, 60)]
    public void 下限より短い指定は下限まで引き上げる(int input, int expected)
    {
        // 短すぎる間隔は対象とDockerのAPIに負荷をかける
        Assert.Equal(expected, CollectionInterval.Normalize(input));
    }

    [Theory]
    [InlineData(7200, 3600)]
    [InlineData(100000, 3600)]
    public void 上限より長い指定は上限まで引き下げる(int input, int expected)
    {
        Assert.Equal(expected, CollectionInterval.Normalize(input));
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(300, 300)]
    [InlineData(3600, 3600)]
    public void 使える値はそのまま通す(int input, int expected)
    {
        Assert.Equal(expected, CollectionInterval.Normalize(input));
    }

    [Theory]
    [InlineData(7 * 60, 6 * 60)]
    [InlineData(59 * 60, 30 * 60)]
    public void 使えない値は要求以下で最も近い値へ丸める(int input, int expected)
    {
        // 丸めた結果が要求より長くなると検知が遅れるため、短い側へ寄せる
        Assert.Equal(expected, CollectionInterval.Normalize(input));
    }

    [Fact]
    public void 丸めた結果は必ずcronで等間隔に表せる()
    {
        // 60の約数でないと、毎時の最後の1回だけ間隔が変わってしまう
        foreach (var seconds in Enumerable.Range(1, 3700))
        {
            var minutes = CollectionInterval.Normalize(seconds) / 60;
            Assert.True(60 % minutes == 0, $"{seconds}秒 → {minutes}分 は60の約数ではありません。");
        }
    }

    [Theory]
    [InlineData(60, "* * * * *")]
    [InlineData(300, "*/5 * * * *")]
    [InlineData(3600, "0 * * * *")]
    public void cron式へ変換する(int seconds, string expected)
    {
        Assert.Equal(expected, CollectionInterval.ToCron(seconds));
    }
}
