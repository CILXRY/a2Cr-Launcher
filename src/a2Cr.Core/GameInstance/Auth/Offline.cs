using System.Text;
using System.Security.Cryptography;

namespace a2Cr.Core.GameInstance.Auth;

public static class Offline
{
    public static string GenerateOfflineUuid(string name)
    {
        // 哈希用户名
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"OfflinePlayer:{name}"));

        // 版本位调整
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);

        // 按 Guid 的字段布局做字节序调整
        Array.Reverse(hash, 0, 4);
        Array.Reverse(hash, 4, 2);
        Array.Reverse(hash, 6, 2);

        return new Guid(hash).ToString();
    }
}