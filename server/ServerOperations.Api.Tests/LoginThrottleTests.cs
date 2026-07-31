using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

/// <summary>
/// ログイン試行の制限。総当たりを実用的でない速さまで落とす。
///
/// 復旧操作にはクールダウン・回数上限・サーキットブレーカーがあるのに、
/// その入口である認証が素通しでは釣り合わない。
/// </summary>
public class LoginThrottleTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly TestTimeProvider _time = new(BaseTime);

    private LoginThrottle CreateSut() => new(_time);

    private static void FailTimes(LoginThrottle sut, int times, string user = "admin", string? ip = "192.0.2.10")
    {
        for (var i = 0; i < times; i++)
        {
            sut.RecordFailure(user, ip);
        }
    }

    // --- 利用者名で数える ---

    [Fact]
    public void 最初の試行は受け付ける()
    {
        Assert.True(CreateSut().Check("admin", "192.0.2.10").Allowed);
    }

    [Fact]
    public void 上限までは受け付ける()
    {
        var sut = CreateSut();

        FailTimes(sut, LoginThrottle.MaxFailuresPerUser);

        Assert.True(sut.Check("admin", "192.0.2.10").Allowed);
    }

    [Fact]
    public void 上限を超えたら止める()
    {
        var sut = CreateSut();

        FailTimes(sut, LoginThrottle.MaxFailuresPerUser + 1);

        Assert.False(sut.Check("admin", "192.0.2.10").Allowed);
    }

    [Fact]
    public void どれだけ待てばよいかを返す()
    {
        var sut = CreateSut();

        FailTimes(sut, LoginThrottle.MaxFailuresPerUser + 1);

        Assert.True(sut.Check("admin", "192.0.2.10").RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void 待ち時間が過ぎれば再び受け付ける()
    {
        var sut = CreateSut();
        FailTimes(sut, LoginThrottle.MaxFailuresPerUser + 1);

        _time.Advance(TimeSpan.FromMinutes(2));

        Assert.True(sut.Check("admin", "192.0.2.10").Allowed);
    }

    [Fact]
    public void 失敗が続くほど待ち時間が延びる()
    {
        var sut = CreateSut();
        FailTimes(sut, LoginThrottle.MaxFailuresPerUser + 1);
        var first = sut.Check("admin", "192.0.2.10").RetryAfter;

        FailTimes(sut, 2);
        var later = sut.Check("admin", "192.0.2.10").RetryAfter;

        Assert.True(later > first);
    }

    [Fact]
    public void 待ち時間には上限がある()
    {
        // 際限なく延ばすと、本人が二度と入れなくなる
        var sut = CreateSut();

        FailTimes(sut, 100);

        Assert.True(sut.Check("admin", "192.0.2.10").RetryAfter <= TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void 一定時間空けば数え直す()
    {
        var sut = CreateSut();
        FailTimes(sut, LoginThrottle.MaxFailuresPerUser + 1);

        _time.Advance(LoginThrottle.Window);

        Assert.True(sut.Check("admin", "192.0.2.10").Allowed);
    }

    [Fact]
    public void 成功したら数え直す()
    {
        var sut = CreateSut();
        FailTimes(sut, LoginThrottle.MaxFailuresPerUser);

        sut.RecordSuccess("admin", "192.0.2.10");
        FailTimes(sut, 1);

        Assert.True(sut.Check("admin", "192.0.2.10").Allowed);
    }

    [Fact]
    public void 利用者名の大小を区別せずに数える()
    {
        // Admin と admin を別枠にすると、上限を素通りできる
        var sut = CreateSut();

        for (var i = 0; i < LoginThrottle.MaxFailuresPerUser + 1; i++)
        {
            sut.RecordFailure(i % 2 == 0 ? "admin" : "ADMIN", "192.0.2.10");
        }

        Assert.False(sut.Check("Admin", "192.0.2.10").Allowed);
    }

    [Fact]
    public void 別の利用者は巻き込まない()
    {
        var sut = CreateSut();

        FailTimes(sut, LoginThrottle.MaxFailuresPerUser + 1, user: "admin", ip: null);

        Assert.True(sut.Check("operator", null).Allowed);
    }

    // --- IPで数える ---

    [Fact]
    public void 同じIPから多数の利用者名を試されたら止める()
    {
        // 利用者名だけで数えると、1つずつ試すスプレー攻撃を止められない
        var sut = CreateSut();

        for (var i = 0; i <= LoginThrottle.MaxFailuresPerIp; i++)
        {
            sut.RecordFailure($"user{i}", "198.51.100.5");
        }

        Assert.False(sut.Check("another-user", "198.51.100.5").Allowed);
    }

    [Fact]
    public void 別のIPは巻き込まない()
    {
        var sut = CreateSut();

        for (var i = 0; i <= LoginThrottle.MaxFailuresPerIp; i++)
        {
            sut.RecordFailure($"user{i}", "198.51.100.5");
        }

        Assert.True(sut.Check("someone", "203.0.113.9").Allowed);
    }

    [Fact]
    public void 成功してもIPの記録は消さない()
    {
        // 自分のアカウントで成功できると、IP側の制限を毎回消せてしまう
        var sut = CreateSut();
        for (var i = 0; i <= LoginThrottle.MaxFailuresPerIp; i++)
        {
            sut.RecordFailure($"user{i}", "198.51.100.5");
        }

        sut.RecordSuccess("own-account", "198.51.100.5");

        Assert.False(sut.Check("another-user", "198.51.100.5").Allowed);
    }

    [Fact]
    public void IPが分からなくても利用者名では数える()
    {
        // プロキシの設定漏れなどでIPが取れない場合でも、無制限にはしない
        var sut = CreateSut();

        FailTimes(sut, LoginThrottle.MaxFailuresPerUser + 1, ip: null);

        Assert.False(sut.Check("admin", null).Allowed);
    }

    // --- 利用者名の存在を漏らさない ---

    [Fact]
    public void 存在しない利用者名でも同じように数える()
    {
        // 存在する名前だけ止まると、止まるかどうかで存在が分かる。
        // 呼び出し側が存在の有無で数え方を変えないことを、ここで固定する。
        var sut = CreateSut();

        FailTimes(sut, LoginThrottle.MaxFailuresPerUser + 1, user: "does-not-exist");

        Assert.False(sut.Check("does-not-exist", "192.0.2.10").Allowed);
    }
}
