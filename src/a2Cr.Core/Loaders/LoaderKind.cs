namespace a2Cr.Core.Loaders;

public enum LoaderKind
{
    Vanilla,
    Fabric,
    Quilt,
    Forge,
    NeoForge
}

public static class LoaderKinds
{
    public static string ToContract(this LoaderKind kind) => kind switch
    {
        LoaderKind.Fabric => "fabric",
        LoaderKind.Quilt => "quilt",
        LoaderKind.Forge => "forge",
        LoaderKind.NeoForge => "neoforge",
        _ => "vanilla"
    };

    public static bool TryParse(string? value, out LoaderKind kind)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "vanilla":
                kind = LoaderKind.Vanilla;
                return true;
            case "fabric":
                kind = LoaderKind.Fabric;
                return true;
            case "quilt":
                kind = LoaderKind.Quilt;
                return true;
            case "forge":
                kind = LoaderKind.Forge;
                return true;
            case "neoforge":
                kind = LoaderKind.NeoForge;
                return true;
            default:
                kind = LoaderKind.Vanilla;
                return false;
        }
    }
}
