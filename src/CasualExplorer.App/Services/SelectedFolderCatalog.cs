using System.Diagnostics;
using System.Runtime.InteropServices;
using CasualExplorer.Models;
using Microsoft.Win32.SafeHandles;

namespace CasualExplorer.Services;

public sealed record SelectedFolderReadResult(FileReadResult Entries, int ScannedCount, bool IsPartial, int SkippedUnsupportedCount);

// Only called after an explicit folder selection. No file contents, recursion,
// link resolution, writes, persisted paths or network APIs.
public static class SelectedFolderCatalog
{
    public const int ScanLimit = 10000;
    public const int ScanBudgetMilliseconds = 2500;
    private const FileAttributes Unsupported = FileAttributes.Offline | (FileAttributes)0x40000 | (FileAttributes)0x400000;

    public static string ValidateLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 3 || !char.IsAsciiLetter(path[0]) ||
            path[1] != ':' || (path[2] != '\\' && path[2] != '/') || path.AsSpan(2).Contains(':') || !Path.IsPathFullyQualified(path))
            throw new UnauthorizedAccessException("Only an absolute local drive path is allowed.");
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string root = Path.GetPathRoot(full)!;
        if (new DriveInfo(root).DriveType is not (DriveType.Fixed or DriveType.Removable))
            throw new UnauthorizedAccessException("Network and virtual drive locations are not supported.");
        return full;
    }

    public static SelectedFolderReadResult Load(string selectedPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string folder = ValidateLocalPath(selectedPath);
        var clock = Stopwatch.StartNew();
        var leases = new List<SafeFileHandle>();
        try
        {
            string root = Path.GetPathRoot(folder)!;
            string current = root;
            string[] parts = Path.GetRelativePath(root, folder).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 64) throw new UnauthorizedAccessException("Path depth exceeds the safety limit.");
            leases.Add(OpenReadOnlyDirectory(current));
            foreach (string part in parts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (part == ".") continue;
                current = Path.Combine(current, part);
                leases.Add(OpenReadOnlyDirectory(current));
            }

            int scanned = 0, normal = 0, links = 0, unsupported = 0;
            bool partial = false;
            var visible = new List<FileItem>(ReadOnlyFileCatalog.DisplayLimit + 1);
            foreach (FileSystemInfo entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (scanned >= ScanLimit || clock.ElapsedMilliseconds >= ScanBudgetMilliseconds) { partial = true; break; }
                scanned++;
                // Enumeration supplies cached metadata. Inspect attributes first;
                // never open the child or query its link target.
                FileAttributes attributes = entry.Attributes;
                if ((attributes & FileAttributes.ReparsePoint) != 0) { links++; continue; }
                if ((attributes & Unsupported) != 0) { unsupported++; continue; }
                normal++;
                bool isFolder = (attributes & FileAttributes.Directory) != 0;
                string extension = Path.GetExtension(entry.Name);
                visible.Add(new FileItem(entry.Name, isFolder ? "フォルダ" : string.IsNullOrEmpty(extension) ? "種類なし" : $"{extension.TrimStart('.').ToUpperInvariant()} ファイル", entry.LastWriteTime));
                visible.Sort((a, b) => { int time = b.ModifiedAt.CompareTo(a.ModifiedAt); return time != 0 ? time : StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name); });
                if (visible.Count > ReadOnlyFileCatalog.DisplayLimit) visible.RemoveAt(visible.Count - 1);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(new FileReadResult(visible.ToArray(), normal, links), scanned, partial, unsupported);
        }
        finally { for (int i = leases.Count - 1; i >= 0; i--) leases[i].Dispose(); }
    }

    private static SafeFileHandle OpenReadOnlyDirectory(string path)
    {
        // OPEN_EXISTING (3), FILE_LIST_DIRECTORY | FILE_READ_ATTRIBUTES (0x81),
        // share READ only (1). Attribute-only access does not enforce the data
        // sharing checks needed to prevent a rename/reparse-point replacement.
        // OPEN_REPARSE_POINT prevents following the final component; retained
        // ancestor handles deny write/delete sharing until enumeration finishes.
        SafeFileHandle handle = OpenExistingDirectory(path, 0x81, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error(); handle.Dispose();
            if (error is 2 or 3) throw new DirectoryNotFoundException("Selected folder is missing.");
            if (error is 5 or 32) throw new UnauthorizedAccessException("Folder cannot be safely leased.");
            throw new IOException("Read-only directory handle failed.", new System.ComponentModel.Win32Exception(error));
        }
        try
        {
            if (!ReadAttributeTag(handle, 9, out AttributeTag tag, 8)) throw new IOException("Directory attribute check failed.");
            var attributes = (FileAttributes)tag.Attributes;
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & (FileAttributes.ReparsePoint | Unsupported)) != 0)
                throw new UnauthorizedAccessException("Linked or offline folder is not supported.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct AttributeTag { public uint Attributes; public uint ReparseTag; }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle OpenExistingDirectory(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadAttributeTag(SafeFileHandle handle, int infoClass, out AttributeTag info, uint size);
}
