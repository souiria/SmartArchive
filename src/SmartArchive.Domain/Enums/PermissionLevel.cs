namespace SmartArchive.Domain.Enums;

[Flags]
public enum PermissionLevel
{
    None = 0,
    Read = 1,
    Download = 2,
    Validate = 4,
    EditMetadata = 8,
    Delete = 16,
    FullControl = Read | Download | Validate | EditMetadata | Delete
}
