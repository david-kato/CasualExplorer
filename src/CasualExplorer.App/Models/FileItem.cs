namespace CasualExplorer.Models;

public sealed class FileItem
{
    public FileItem(string name, string kind, DateTime modifiedAt)
    {
        Name = name;
        Kind = kind;
        ModifiedAt = modifiedAt;
    }

    public string Name { get; }

    public string Kind { get; }

    public DateTime ModifiedAt { get; }

    public string ModifiedAtText => ModifiedAt.ToString("yyyy/MM/dd HH:mm");
}

