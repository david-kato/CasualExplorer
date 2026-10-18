using CasualExplorer.Models;

namespace CasualExplorer.Services;

public sealed class FileReadResult
{
    public FileReadResult(IReadOnlyList<FileItem> files, int totalFileCount, int skippedLinkCount = 0)
    {
        Files = files;
        TotalFileCount = totalFileCount;
        SkippedLinkCount = skippedLinkCount;
    }

    public IReadOnlyList<FileItem> Files { get; }

    public int TotalFileCount { get; }
    public int SkippedLinkCount { get; }
}
