using System.Security.Cryptography;
using System.Text;
using ServerOperations.Core.Services.Backup;

namespace ServerOperations.Api.Tests;

/// <summary>
/// バックアップの暗号化形式を固定する。
///
/// 復元の道具(`scripts/restore-backup.py`)はこの形式を前提に、
/// C#とは別の実装でAES-GCMを解く。**片方だけ変えると復号できなくなる。**
/// 形式を変えるときは道具も一緒に変えること。
///
/// バックアップは戻せて初めて価値がある。ここが崩れると、
/// 取れているように見えて中身が二度と読めない状態になる。
/// </summary>
public class BackupFormatTests
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    [Fact]
    public void 出力は_nonce12_tag16_暗号文_の順で並ぶ()
    {
        var plaintext = Encoding.UTF8.GetBytes("""{"version":1}""");

        var encrypted = BackupService.Encrypt(plaintext, "key");

        Assert.Equal(NonceSize + TagSize + plaintext.Length, encrypted.Length);
    }

    [Fact]
    public void 鍵は任意長の文字列をSHA256で導出する()
    {
        // 道具側も同じ導出をする。ここを変えると過去のバックアップが開かなくなる
        var plaintext = Encoding.UTF8.GetBytes("hello");
        var key = "任意の長さでよい鍵";

        var encrypted = BackupService.Encrypt(plaintext, key);

        var derived = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var nonce = encrypted.AsSpan(0, NonceSize);
        var tag = encrypted.AsSpan(NonceSize, TagSize);
        var ciphertext = encrypted.AsSpan(NonceSize + TagSize);
        var decrypted = new byte[ciphertext.Length];

        using var aes = new AesGcm(derived, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, decrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void 同じ内容でも毎回違う暗号文になる()
    {
        // nonceを固定すると、同じ鍵での再利用でGCMの安全性が壊れる
        var plaintext = Encoding.UTF8.GetBytes("same content");

        var first = BackupService.Encrypt(plaintext, "key");
        var second = BackupService.Encrypt(plaintext, "key");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void 往復して元に戻る()
    {
        var plaintext = Encoding.UTF8.GetBytes("""{"users":[{"username":"admin"}]}""");

        var restored = BackupService.Decrypt(BackupService.Encrypt(plaintext, "key"), "key");

        Assert.Equal(plaintext, restored);
    }

    [Fact]
    public void 鍵が違うと復号できない()
    {
        var encrypted = BackupService.Encrypt(Encoding.UTF8.GetBytes("secret"), "key");

        Assert.Throws<AuthenticationTagMismatchException>(
            () => BackupService.Decrypt(encrypted, "違う鍵"));
    }

    [Fact]
    public void 改竄された暗号文は復号できない()
    {
        var encrypted = BackupService.Encrypt(Encoding.UTF8.GetBytes("secret"), "key");
        encrypted[^1] ^= 0xFF;

        Assert.Throws<AuthenticationTagMismatchException>(
            () => BackupService.Decrypt(encrypted, "key"));
    }

    [Fact]
    public void 短すぎる入力は形式の誤りとして弾く()
    {
        Assert.Throws<CryptographicException>(
            () => BackupService.Decrypt(new byte[NonceSize + TagSize - 1], "key"));
    }
}
