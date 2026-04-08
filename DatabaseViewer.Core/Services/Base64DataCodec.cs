using System.Text;

namespace DatabaseViewer.Core.Services;

/// <summary>
/// Serializes sensitive values as Base64 text so the storage format stays cross-platform.
/// </summary>
public sealed class Base64DataCodec
{
    /// <summary>
    /// Encodes plain text as Base64 without encryption.
    /// </summary>
    public string Encode(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(plainText);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Decodes Base64 text back into plain text.
    /// </summary>
    public string Decode(string base64Text)
    {
        if (string.IsNullOrEmpty(base64Text))
        {
            return string.Empty;
        }

        var bytes = Convert.FromBase64String(base64Text);
        return Encoding.UTF8.GetString(bytes);
    }
}