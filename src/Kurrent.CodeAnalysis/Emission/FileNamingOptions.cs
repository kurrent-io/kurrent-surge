namespace Kurrent.CodeAnalysis.Emission;

public sealed class FileNamingOptions {
    public static FileNamingOptions Default { get; } = new() {
        UseFoldersForNamespace = true,
        UseFoldersForPrefix    = true,
        LowercasePath          = false,
        Prefix                 = ""
    };

    public static FileNamingOptions Flat => new() { UseFoldersForNamespace = false, UseFoldersForPrefix = false };

    public static FileNamingOptions Lowercase =>
        new() {
            UseFoldersForNamespace = true,
            UseFoldersForPrefix    = true,
            LowercasePath          = true
        };

    public bool   UseFoldersForNamespace { get; set; } = true;
    public bool   UseFoldersForPrefix    { get; set; } = true;
    public bool   LowercasePath          { get; set; }
    public string Prefix                 { get; set; } = "";
}