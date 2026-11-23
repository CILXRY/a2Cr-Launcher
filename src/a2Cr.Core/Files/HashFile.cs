namespace a2Cr.Core.Files;

public class HashFile
{
    public static string HashFileSha1(string filePath)
    {
        var file = File.OpenRead(filePath);
        var fileSha1 = System.Security.Cryptography.SHA1.HashData(file);

        return Convert.ToHexString(fileSha1);
    }
}