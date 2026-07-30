using System.Net;
using System.Net.Http.Json;
using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Models.Auth;

namespace ServerOperations.Api.Tests;

/// <summary>
/// 役割ごとの認可をHTTP経由で確かめる。
///
/// 画面の出し分けは利用者の利便性のためであり、権限の担保はAPI側で行う設計にしている。
/// その担保が実際に効いていることをここで確かめる。
/// 画面を通さずに直接APIを叩いても弾かれる、というのが要点。
/// </summary>
public class AuthorizationEndpointsTests(AuthorizedApiFactory factory)
    : IClassFixture<AuthorizedApiFactory>
{
    /// <summary>
    /// 運用管理者 + MFA再認証を要求する口。
    /// いずれも設定変更・復旧・監査参照といった影響のある操作。
    /// </summary>
    public static TheoryData<string, string> AdminOnlyEndpoints()
    {
        var data = new TheoryData<string, string>();
        data.Add("GET", "/api/v1/settings/profile");
        data.Add("GET", "/api/v1/settings/retention");
        data.Add("GET", "/api/v1/settings/network-cidrs");
        data.Add("GET", "/api/v1/settings/secrets/smtp-password/status");
        data.Add("GET", "/api/v1/settings/backup/runs");
        data.Add("GET", "/api/v1/settings/notification");
        data.Add("GET", "/api/v1/settings/backup-settings");
        data.Add("GET", "/api/v1/audit-logs");
        data.Add("GET", "/api/v1/audit-logs/export");
        data.Add("GET", "/api/v1/maintenance-windows");
        data.Add("GET", "/api/v1/audit-logs/filter-options");
        return data;
    }

    /// <summary>ログイン済みなら役割を問わず読める口。</summary>
    public static TheoryData<string> AuthenticatedEndpoints() =>
    [
        "/api/v1/adapter-templates",
        "/api/v1/recovery-action-catalog",
        "/api/v1/diagnostic-rules/editor-options",
        "/api/v1/insights/operations",
    ];

    // --- 未認証 ---

    [Theory]
    [MemberData(nameof(AdminOnlyEndpoints))]
    public async Task 管理者専用の口は未認証で401(string method, string path)
    {
        using var client = factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AuthenticatedEndpoints))]
    public async Task ログインが要る口は未認証で401(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- 閲覧者が管理者専用の口を叩く ---

    [Theory]
    [MemberData(nameof(AdminOnlyEndpoints))]
    public async Task 閲覧者は管理者専用の口を叩けない(string method, string path)
    {
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyEndpoints))]
    public async Task 実行専用の役割も管理者専用の口を叩けない(string method, string path)
    {
        using var client = factory.CreateClientAs(UserRole.SystemExecutor);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- 閲覧者が変更操作を試みる ---

    [Fact]
    public async Task 閲覧者は監視対象を作れない()
    {
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.PostAsJsonAsync("/api/v1/targets", new
        {
            name = "勝手に追加した対象",
            templateId = "docker-host",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 閲覧者は監視対象を書き換えられない()
    {
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.PutAsJsonAsync("/api/v1/targets/1", new
        {
            name = "書き換え",
            isEnabled = true,
            autoRecoveryEnabled = true,
            allowedContainers = new[] { "web" },
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 閲覧者は接続試験を実行できない()
    {
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.PostAsync("/api/v1/targets/1/test-connection", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 閲覧者は復旧を要求できない()
    {
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/incidents/1/recovery-actions")
        {
            Content = JsonContent.Create(new
            {
                actionId = "RESTART_ALLOWED_CONTAINER",
                targetResource = "web",
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 閲覧者は復旧を承認できない()
    {
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.PostAsJsonAsync("/api/v1/incidents/1/approvals", new
        {
            actionId = "STOP_ALLOWED_CONTAINER",
            approve = true,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 閲覧者はインシデントの状態を変えられない()
    {
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.PatchAsJsonAsync("/api/v1/incidents/1/status", new
        {
            status = "Closed",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 閲覧者は診断ルールを作れない()
    {
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.PostAsJsonAsync("/api/v1/diagnostic-rules", new
        {
            name = "勝手に追加したルール",
            classification = "X",
            ruleType = "State",
            conditionJson = """{"field":"containerState","equalsAny":["exited"]}""",
            severity = "High",
            rationaleTemplate = "{field}",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 閲覧者は診断ルールを止められない()
    {
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.PatchAsJsonAsync(
            "/api/v1/diagnostic-rules/1/enabled", new { isEnabled = false });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 閲覧者はAIの利用設定を変えられない()
    {
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.PatchAsJsonAsync("/api/v1/ai-usage/enabled", new
        {
            isEnabled = true,
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 閲覧者はメンテナンス期間を登録できない()
    {
        // 抑止の設定は自動復旧の挙動を変える操作にあたる
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.PostAsJsonAsync("/api/v1/maintenance-windows", new
        {
            reason = "勝手に登録した期間",
            startsAt = DateTime.UtcNow,
            endsAt = DateTime.UtcNow.AddHours(1),
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 閲覧者は対応メモを書けない()
    {
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.PostAsJsonAsync("/api/v1/incidents/1/notes", new
        {
            body = "勝手に書いたメモ",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 閲覧者はテスト通知を送れない()
    {
        // 送信を起こさせない(繰り返し叩かれると外部へ大量に送ることになる)
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.PostAsync("/api/v1/settings/notification/test", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 閲覧者は秘密情報を書き換えられない()
    {
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.PutAsJsonAsync("/api/v1/settings/secrets/ai-api-key", new
        {
            value = "勝手に入れた値",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 閲覧者はバックアップを実行できない()
    {
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.PostAsync("/api/v1/settings/backup/run", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 閲覧者はAIによる再診断を要求できない()
    {
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.PostAsync("/api/v1/incidents/1/rediagnose", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 閲覧者はルールの試験を実行できない()
    {
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.PostAsJsonAsync("/api/v1/diagnostic-rules/test", new
        {
            containerState = "exited",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- 役割が足りていれば通ること ---

    [Theory]
    [MemberData(nameof(AuthenticatedEndpoints))]
    public async Task ログインしていれば閲覧者でも読める口がある(string path)
    {
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.GetAsync(path);

        // 役割で弾かれないことを確かめる(DBが必要な口は別の理由で失敗しうるため401/403のみを見る)
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 管理者は管理者専用の口で役割によって弾かれない()
    {
        using var client = factory.CreateClientAs(UserRole.OperatorAdmin);

        var response = await client.GetAsync("/api/v1/recovery-action-catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// 「閲覧者は弾かれる」だけでは、全部が弾かれているのか役割で分かれているのか区別できない。
    /// 同じ口を管理者で叩いて通ることを確かめ、対比で役割判定が効いていることを示す。
    /// </summary>
    [Theory]
    [MemberData(nameof(AdminOnlyEndpoints))]
    public async Task 管理者なら管理者専用の口で権限不足にならない(string method, string path)
    {
        using var client = factory.CreateClientAs(UserRole.OperatorAdmin);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        // DBが必要な口はこの環境では別の理由で失敗するため、
        // ここでは「認可で弾かれていないこと」だけを見る
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- 自分のアカウント操作 ---

    [Fact]
    public async Task パスワード変更は未認証で401()
    {
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/v1/me/password", new
        {
            currentPassword = "x",
            newPassword = "yyyyyyyyyyyy",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task パスワード変更は役割を問わず行える()
    {
        // 自分のパスワードは誰でも変えられる必要がある。
        // 管理者専用にすると、閲覧者が初期パスワードを変えられない。
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.PutAsJsonAsync("/api/v1/me/password", new
        {
            currentPassword = "x",
            newPassword = "yyyyyyyyyyyy",
        });

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MFAの設定は役割を問わず行える()
    {
        // MFAを設定できないと管理操作に進めないため、ここで役割を要求しない
        using var client = factory.CreateClientAs(UserRole.Viewer);

        var response = await client.PostAsync("/api/v1/auth/mfa/setup", content: null);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MFAの再認証が古くてもパスワードは変えられる()
    {
        // 再認証が切れて詰むのを防ぐ
        using var staleFactory = new AuthorizedApiFactory { MfaRecentlyVerified = false };
        using var client = staleFactory.CreateClientAs(UserRole.OperatorAdmin);

        var response = await client.PutAsJsonAsync("/api/v1/me/password", new
        {
            currentPassword = "x",
            newPassword = "yyyyyyyyyyyy",
        });

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- MFA再認証の要求 ---

    /// <summary>
    /// 管理操作は役割だけでなくMFAの直近認証も要求する。
    /// 役割が足りていても、再認証が古ければ通さない。
    /// </summary>
    [Theory]
    [MemberData(nameof(AdminOnlyEndpoints))]
    public async Task MFAの再認証が古ければ管理者でも弾かれる(string method, string path)
    {
        // このテストだけMFAの状態を変えるため、専用の土台を立てる
        using var staleFactory = new AuthorizedApiFactory { MfaRecentlyVerified = false };
        using var client = staleFactory.CreateClientAs(UserRole.OperatorAdmin);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MFAの再認証が古ければ復旧も要求できない()
    {
        using var staleFactory = new AuthorizedApiFactory { MfaRecentlyVerified = false };
        using var client = staleFactory.CreateClientAs(UserRole.OperatorAdmin);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/incidents/1/recovery-actions")
        {
            Content = JsonContent.Create(new
            {
                actionId = "RESTART_ALLOWED_CONTAINER",
                targetResource = "web",
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MFAの再認証が古くても参照だけの口は読める()
    {
        // 再認証を求めるのは影響のある操作に限る。読むだけの口は妨げない。
        using var staleFactory = new AuthorizedApiFactory { MfaRecentlyVerified = false };
        using var client = staleFactory.CreateClientAs(UserRole.Viewer);

        var response = await client.GetAsync("/api/v1/recovery-action-catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
