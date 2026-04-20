namespace DatabaseViewer.Core.Models;

/// <summary>保存 SQLite / SQLCipher 的加密连接设置。</summary>
public sealed class SqliteCipherOptions
{
    public bool Enabled { get; set; }

    public string Password { get; set; } = string.Empty;

    public SqliteCipherKeyFormat KeyFormat { get; set; } = SqliteCipherKeyFormat.Passphrase;

    public int? PageSize { get; set; }

    public int? KdfIter { get; set; }

    public int? CipherCompatibility { get; set; }

    public int? PlaintextHeaderSize { get; set; }

    public bool? UseHmac { get; set; }

    public string KdfAlgorithm { get; set; } = string.Empty;

    public string HmacAlgorithm { get; set; } = string.Empty;
}