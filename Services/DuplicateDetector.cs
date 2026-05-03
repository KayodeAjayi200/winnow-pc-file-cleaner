using System.Security.Cryptography;
using FileTinder.Models;

namespace FileTinder.Services;

/// <summary>
/// Two-pass duplicate detector:
/// 1. Group by exact file size (no I/O — free).
/// 2. Hash the first 64 KB of each candidate (cheap even for huge files).
/// Sets <see cref="FileItem.IsDuplicate"/>, <see cref="FileItem.DuplicateCount"/>,
/// and <see cref="FileItem.DuplicateGroupKey"/> on every file in a duplicate group.
/// </summary>
public static class DuplicateDetector
{
    public static void MarkDuplicates(List<FileItem> files, CancellationToken ct = default)
    {
        // Reset previous state
        foreach (var f in files)
        {
            f.IsDuplicate       = false;
            f.DuplicateCount    = 0;
            f.DuplicateGroupKey = null;
        }

        // Pass 1: group by exact size (zero I/O)
        var sizeCandidates = files
            .Where(f => f.Size > 0)
            .GroupBy(f => f.Size)
            .Where(g => g.Count() > 1);

        foreach (var sizeGroup in sizeCandidates)
        {
            ct.ThrowIfCancellationRequested();

            // Pass 2: hash first 64 KB in parallel within the size group
            var byHash = sizeGroup
                .AsParallel()
                .WithCancellation(ct)
                .Select(f => (file: f, hash: HashFirst64KB(f.FullPath)))
                .GroupBy(x => x.hash)
                .Where(g => g.Count() > 1);

            foreach (var hashGroup in byHash)
            {
                var groupKey = hashGroup.Key;
                var count    = hashGroup.Count();
                foreach (var (file, _) in hashGroup)
                {
                    file.IsDuplicate       = true;
                    file.DuplicateCount    = count;
                    file.DuplicateGroupKey = groupKey;
                }
            }
        }
    }

    private static string HashFirst64KB(string path)
    {
        try
        {
            var buffer = new byte[65_536];
            using var fs = File.OpenRead(path);
            var read = fs.Read(buffer, 0, buffer.Length);
            return Convert.ToHexString(MD5.HashData(buffer.AsSpan(0, read)));
        }
        catch
        {
            // Treat unreadable files as unique to avoid false positives
            return Guid.NewGuid().ToString("N");
        }
    }
}
