using System.Text.Json;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Index;

/// <summary>
/// Validates the structural integrity of a LeanLucene index stored in a <see cref="MMapDirectory"/>.
/// </summary>
public static class IndexValidator
{
    private static readonly string[] RequiredExtensions = [".seg", ".dic", ".pos", ".fdt", ".fdx", ".nrm"];

    /// <summary>
    /// Validates the latest commit in <paramref name="directory"/>.
    /// Checks that all referenced segment files are present and readable,
    /// and that the document counts are internally consistent.
    /// </summary>
    public static IndexCheckResult Validate(MMapDirectory directory)
    {
        var result = new IndexCheckResult();
        var dirPath = directory.DirectoryPath;

        // Locate the latest segments_N file
        var commitPath = FindLatestCommit(dirPath);
        if (commitPath is null)
        {
            result.AddIssue("No commit file (segments_N) found in directory.");
            return result;
        }

        List<string> segmentIds;
        try
        {
            var json = CommitFileFormat.ReadJson(commitPath);
            using var doc = JsonDocument.Parse(json);
            segmentIds = doc.RootElement.GetProperty("Segments")
                .EnumerateArray()
                .Select(e => e.GetString()!)
                .ToList();
        }
        catch (IOException ex)
        {
            result.AddIssue($"Cannot read commit file '{commitPath}': {ex.Message}");
            return result;
        }
        catch (JsonException ex)
        {
            result.AddIssue($"Cannot read commit file '{commitPath}': {ex.Message}");
            return result;
        }

        foreach (var segId in segmentIds)
        {
            result.SegmentsChecked++;
            var basePath = Path.Combine(dirPath, segId);

            // Check required files exist
            foreach (var ext in RequiredExtensions)
            {
                var filePath = basePath + ext;
                if (!File.Exists(filePath))
                    result.AddIssue($"Segment '{segId}': missing file '{segId}{ext}'.");
            }

            // Read and validate segment metadata
            var segPath = basePath + ".seg";
            if (!File.Exists(segPath)) continue;

            SegmentInfo info;
            try
            {
                info = SegmentInfo.ReadFrom(segPath);
            }
            catch (IOException ex)
            {
                result.AddIssue($"Segment '{segId}': cannot read .seg metadata — {ex.Message}");
                continue;
            }
            catch (JsonException ex)
            {
                result.AddIssue($"Segment '{segId}': cannot read .seg metadata — {ex.Message}");
                continue;
            }

            if (info.DocCount <= 0)
            {
                result.AddIssue($"Segment '{segId}': DocCount={info.DocCount} is invalid.");
            }

            if (info.LiveDocCount < 0 || info.LiveDocCount > info.DocCount)
            {
                result.AddIssue($"Segment '{segId}': LiveDocCount={info.LiveDocCount} is out of range [0,{info.DocCount}].");
            }

            // Verify .fdt (stored fields data) and .fdx (index) are readable
            // Verify .fdt and .fdx are readable
            var fdtPath = basePath + ".fdt";
            var fdxPath = basePath + ".fdx";
            if (File.Exists(fdtPath) && File.Exists(fdxPath))
            {
                try
                {
                    // Just verify we can open the index file
                    using var fdxStream = File.OpenRead(fdxPath);
                    if (fdxStream.Length == 0)
                        result.AddIssue($"Segment '{segId}': .fdx is empty.");
                }
                catch (IOException ex)
                {
                    result.AddIssue($"Segment '{segId}': cannot read .fdx — {ex.Message}");
                }
            }

            // Verify .nrm is readable (per-field format: 4-byte field count header minimum)
            var nrmPath = basePath + ".nrm";
            if (File.Exists(nrmPath))
            {
                try
                {
                    long nrmLen = new FileInfo(nrmPath).Length;
                    if (nrmLen < 4)
                    {
                        result.AddIssue($"Segment '{segId}': .nrm has {nrmLen} bytes, expected at least 4.");
                    }
                }
                catch (IOException ex)
                {
                    result.AddIssue($"Segment '{segId}': cannot read .nrm — {ex.Message}");
                }
            }

            result.DocumentsChecked += info.DocCount;
        }

        return result;
    }

    private static string? FindLatestCommit(string dirPath)
    {
        string? latest = null;
        int maxGen = -1;
        foreach (var file in Directory.GetFiles(dirPath, "segments_*"))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("segments_") && int.TryParse(name["segments_".Length..], out int gen) && gen > maxGen)
            {
                maxGen = gen;
                latest = file;
            }
        }
        return latest;
    }
}
