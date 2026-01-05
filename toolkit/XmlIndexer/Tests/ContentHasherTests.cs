namespace XmlIndexer.Tests;

/// <summary>
/// Manual unit tests for ContentHasher.
/// Run with: dotnet run -- test-hasher
/// </summary>
public static class ContentHasherTests
{
    public static int Run()
    {
        Console.WriteLine("=== ContentHasher Tests ===\n");
        int passed = 0, failed = 0;

        // Test 1: Same content = same hash
        Test("HashString consistency", () =>
        {
            var hash1 = Utils.ContentHasher.HashString("hello world");
            var hash2 = Utils.ContentHasher.HashString("hello world");
            return hash1 == hash2 ? null : $"Hashes differ: {hash1} vs {hash2}";
        }, ref passed, ref failed);

        // Test 2: Different content = different hash
        Test("HashString detects changes", () =>
        {
            var hash1 = Utils.ContentHasher.HashString("hello world");
            var hash2 = Utils.ContentHasher.HashString("hello world!");
            return hash1 != hash2 ? null : "Hashes should differ for different content";
        }, ref passed, ref failed);

        // Test 3: Hash is 64 characters (SHA256 = 256 bits = 64 hex chars)
        Test("HashString returns 64 char hex", () =>
        {
            var hash = Utils.ContentHasher.HashString("test");
            if (hash.Length != 64) return $"Expected 64 chars, got {hash.Length}";
            if (!System.Text.RegularExpressions.Regex.IsMatch(hash, "^[A-F0-9]+$"))
                return "Hash should be uppercase hex";
            return null;
        }, ref passed, ref failed);

        // Test 4: Empty string has consistent hash
        Test("HashString handles empty string", () =>
        {
            var hash1 = Utils.ContentHasher.HashString("");
            var hash2 = Utils.ContentHasher.HashString("");
            if (hash1 != hash2) return "Empty string hashes should match";
            if (hash1.Length != 64) return "Empty string should still produce 64 char hash";
            return null;
        }, ref passed, ref failed);

        // Test 5: HashFile works on real file
        Test("HashFile on real file", () =>
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, "test content for hashing");
                var hash = Utils.ContentHasher.HashFile(tempFile);
                if (hash.Length != 64) return $"Expected 64 char SHA256, got {hash.Length}";
                return null;
            }
            finally
            {
                File.Delete(tempFile);
            }
        }, ref passed, ref failed);

        // Test 6: HashFile detects content changes
        Test("HashFile detects modifications", () =>
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, "original content");
                var hash1 = Utils.ContentHasher.HashFile(tempFile);
                File.WriteAllText(tempFile, "modified content");
                var hash2 = Utils.ContentHasher.HashFile(tempFile);
                return hash1 != hash2 ? null : "File hash should change when content modified";
            }
            finally
            {
                File.Delete(tempFile);
            }
        }, ref passed, ref failed);

        // Test 7: HashFolder detects file changes
        Test("HashFolder detects file modifications", () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(tempDir);
                File.WriteAllText(Path.Combine(tempDir, "test.txt"), "original");
                var hash1 = Utils.ContentHasher.HashFolder(tempDir);
                File.WriteAllText(Path.Combine(tempDir, "test.txt"), "modified");
                var hash2 = Utils.ContentHasher.HashFolder(tempDir);
                return hash1 != hash2 ? null : "Folder hash should change when file modified";
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }, ref passed, ref failed);

        // Test 8: HashFolder detects new files
        Test("HashFolder detects new files", () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(tempDir);
                File.WriteAllText(Path.Combine(tempDir, "test.txt"), "content");
                var hash1 = Utils.ContentHasher.HashFolder(tempDir);
                File.WriteAllText(Path.Combine(tempDir, "test2.txt"), "content2");
                var hash2 = Utils.ContentHasher.HashFolder(tempDir);
                return hash1 != hash2 ? null : "Folder hash should change when file added";
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }, ref passed, ref failed);

        // Test 9: HashFolder detects deleted files
        Test("HashFolder detects deleted files", () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(tempDir);
                File.WriteAllText(Path.Combine(tempDir, "test.txt"), "content");
                File.WriteAllText(Path.Combine(tempDir, "test2.txt"), "content2");
                var hash1 = Utils.ContentHasher.HashFolder(tempDir);
                File.Delete(Path.Combine(tempDir, "test2.txt"));
                var hash2 = Utils.ContentHasher.HashFolder(tempDir);
                return hash1 != hash2 ? null : "Folder hash should change when file deleted";
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }, ref passed, ref failed);

        // Test 10: HashFolder with pattern
        Test("HashFolder respects file pattern", () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(tempDir);
                File.WriteAllText(Path.Combine(tempDir, "file.xml"), "<root/>");
                File.WriteAllText(Path.Combine(tempDir, "file.txt"), "text");
                var hashXml = Utils.ContentHasher.HashFolder(tempDir, "*.xml");
                var hashAll = Utils.ContentHasher.HashFolder(tempDir, "*");
                return hashXml != hashAll ? null : "Pattern filter should produce different hash";
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }, ref passed, ref failed);

        // Test 11: HashFolder handles empty folder
        Test("HashFolder handles empty folder", () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(tempDir);
                var hash = Utils.ContentHasher.HashFolder(tempDir);
                if (string.IsNullOrEmpty(hash)) return "Empty folder should produce a hash";
                if (hash.Length != 64) return "Empty folder hash should be 64 chars";
                return null;
            }
            finally
            {
                Directory.Delete(tempDir);
            }
        }, ref passed, ref failed);

        // Test 12: HashStrings combines multiple values
        Test("HashStrings combines values", () =>
        {
            var hash1 = Utils.ContentHasher.HashStrings("a", "b");
            var hash2 = Utils.ContentHasher.HashStrings("ab");
            var hash3 = Utils.ContentHasher.HashStrings("a", "b");
            if (hash1 == hash2) return "HashStrings('a','b') should differ from HashStrings('ab')";
            if (hash1 != hash3) return "HashStrings should be consistent";
            return null;
        }, ref passed, ref failed);

        Console.WriteLine($"\n=== Results: {passed} passed, {failed} failed ===");
        return failed > 0 ? 1 : 0;
    }

    private static void Test(string name, Func<string?> test, ref int passed, ref int failed)
    {
        try
        {
            var error = test();
            if (error == null)
            {
                Console.WriteLine($"  ✓ {name}");
                passed++;
            }
            else
            {
                Console.WriteLine($"  ✗ {name}: {error}");
                failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ {name}: EXCEPTION - {ex.Message}");
            failed++;
        }
    }
}
