namespace a2Cr.Core.Download;

public record DownloadItem(string Url, string TargetPath, string? Sha1, long Size);
