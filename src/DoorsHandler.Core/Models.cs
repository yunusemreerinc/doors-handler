namespace DoorsHandler.Core;

/// <summary>Kind of an item in the DOORS database tree, as reported by DXL <c>type(Item)</c>.</summary>
public enum DoorsItemType
{
    Unknown = 0,
    Folder,
    Project,
    Formal,
    Link,
    Descriptive,
}

/// <summary>A folder, project or module found in the DOORS database.</summary>
public sealed record DoorsItem(
    DoorsItemType Type,
    string Name,
    string FullName,
    string UniqueId,
    string Description)
{
    /// <summary>True for formal, link and descriptive modules.</summary>
    public bool IsModule => Type is DoorsItemType.Formal or DoorsItemType.Link or DoorsItemType.Descriptive;

    /// <summary>Full path of the folder/project that contains this item ("/" for top-level items).</summary>
    public string ParentPath
    {
        get
        {
            var index = FullName.LastIndexOf('/');
            return index <= 0 ? "/" : FullName[..index];
        }
    }

    public static DoorsItemType ParseType(string value) => value.Trim() switch
    {
        "Folder" => DoorsItemType.Folder,
        "Project" => DoorsItemType.Project,
        "Formal" => DoorsItemType.Formal,
        "Link" => DoorsItemType.Link,
        "Descriptive" => DoorsItemType.Descriptive,
        _ => DoorsItemType.Unknown,
    };
}

/// <summary>Information about the DOORS client session we are attached to.</summary>
public sealed record DoorsSessionInfo(string UserName, string DatabaseName, string Version);

/// <summary>Result of a module listing: the items plus any non-fatal warnings DOORS reported.</summary>
public sealed record DoorsListing(IReadOnlyList<DoorsItem> Items, IReadOnlyList<string> Warnings);

/// <summary>Raised when DOORS cannot be reached or a DXL script reports an error.</summary>
public class DoorsException : Exception
{
    public DoorsException(DoorsErrorKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        if (inner is not null) HResult = inner.HResult;
    }

    /// <summary>Why the operation failed; drives the explanation shown to the user.</summary>
    public DoorsErrorKind Kind { get; }
}
