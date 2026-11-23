using System.Security.Cryptography;

namespace RiotAccountHub.Core.Snapshots;

/// <summary>
/// 加密/解密的介面。正式程式用 DPAPI，測試時也可以換成別的實作。
/// </summary>
public interface IDataProtector
{
    byte[] Protect(byte[] plain);
    byte[] Unprotect(byte[] encrypted);
}

/// <summary>
/// 使用 Windows DPAPI(ProtectedData)加密。
///
/// 什麼是 DPAPI?
///   Windows 內建的加密功能，金鑰綁定「目前登入的 Windows 使用者」。
///   本程式不需要自己保管任何金鑰；加密後的檔案就算被複製到別台電腦、
///   或被同一台電腦的其他 Windows 使用者拿到，也無法解密。
/// </summary>
public sealed class DpapiProtector : IDataProtector
{
    // 額外的「鹽」：讓這些資料只有本程式才解得開（其他也用 DPAPI 的程式無法直接解）
    private static readonly byte[] DefaultEntropy = "RiotAccountHub.Snapshot.v1"u8.ToArray();

    private readonly byte[] _entropy;

    public DpapiProtector(byte[]? entropy = null)
    {
        _entropy = entropy ?? DefaultEntropy;
    }

    public byte[] Protect(byte[] plain) =>
        ProtectedData.Protect(plain, _entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] encrypted) =>
        ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.CurrentUser);
}
