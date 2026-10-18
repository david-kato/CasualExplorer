using CasualExplorer.Models;

namespace CasualExplorer.Services;

public static class ReadOnlyFileCatalog
{
    public const int DisplayLimit = 30;

    public static FileReadResult LoadBundledSamples()
    {
        string baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        string sampleFolder = Path.GetFullPath(
            Path.Combine(baseDirectory, "Samples", "TestData"));

        string relativePath = Path.GetRelativePath(baseDirectory, sampleFolder);
        bool isOutsideBaseDirectory = Path.IsPathRooted(relativePath)
            || relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

        if (isOutsideBaseDirectory)
        {
            throw new UnauthorizedAccessException("The bundled sample folder is outside the application directory.");
        }

        if (!Directory.Exists(sampleFolder))
        {
            throw new DirectoryNotFoundException(sampleFolder);
        }

        // Reject junctions/symbolic links rather than following them outside the bundle.
        foreach (string folder in new[] { baseDirectory, Path.Combine(baseDirectory, "Samples"), sampleFolder })
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException("The bundled sample path contains a reparse point.");
            }
        }

        FileInfo[] allFiles = new DirectoryInfo(sampleFolder).GetFiles("*", SearchOption.TopDirectoryOnly);

        // Inspect only entry attributes. Never resolve or read a link target.
        FileInfo[] normalFiles = allFiles
            .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0)
            .ToArray();
        int skippedLinkCount = allFiles.Length - normalFiles.Length;

        IReadOnlyList<FileItem> visibleFiles = normalFiles
            .OrderByDescending(file => file.LastWriteTime)
            .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(DisplayLimit)
            .Select(file => new FileItem(
                file.Name,
                GetKind(file.Extension),
                file.LastWriteTime))
            .ToArray();

        return new FileReadResult(visibleFiles, normalFiles.Length, skippedLinkCount);
    }

    private static string GetKind(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "種類なし";
        }

        return $"{extension.TrimStart('.').ToUpperInvariant()} ファイル";
    }
}
