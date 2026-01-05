using System.Security.Cryptography;
using System.Text;

namespace XmlIndexer.Utils;

/// <summary>
/// SHA256-based content hashing for incremental processing.
/// Used to detect file changes and skip unchanged content during analysis.
/// </summary>
public static class ContentHasher
{
    /// <summary>
    /// Hash a single file's contents using SHA256.
    /// Returns a 64-character hex string.
    /// </summary>
    public static string HashFile(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Hash all files matching pattern in a folder (recursive).
    /// Returns combined hash of all file paths + contents.
    /// Detects: file modifications, additions, deletions, renames.
    /// </summary>
    /// <param name="path">Folder path to hash</param>
    /// <param name="pattern">File pattern (e.g., "*.xml", "*.cs")</param>
    public static string HashFolder(string path, string pattern = "*")
    {
        if (!Directory.Exists(path))
            return string.Empty;

        using var sha256 = SHA256.Create();

        var files = Directory.GetFiles(path, pattern, SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)  // Consistent ordering across runs
            .ToList();

        if (files.Count == 0)
        {
            // Empty folder - return hash of empty input
            sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(sha256.Hash!);
        }

        foreach (var file in files)
        {
            // Include relative path in hash (detects renames/moves)
            var relativePath = Path.GetRelativePath(path, file);
            var pathBytes = Encoding.UTF8.GetBytes(relativePath.ToLowerInvariant());
            sha256.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);

            // Include file content
            var contentBytes = File.ReadAllBytes(file);
            sha256.TransformBlock(contentBytes, 0, contentBytes.Length, null, 0);
        }

        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha256.Hash!);
    }

    /// <summary>
    /// Hash a string value using SHA256.
    /// Returns a 64-character hex string.
    /// </summary>
    public static string HashString(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Hash multiple strings together (e.g., for composite keys).
    /// </summary>
    public static string HashStrings(params string[] values)
    {
        var combined = string.Join("\0", values.Select(v => v ?? string.Empty));
        return HashString(combined);
    }
}
