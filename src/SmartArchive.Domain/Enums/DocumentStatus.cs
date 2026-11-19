namespace SmartArchive.Domain.Enums;

public enum DocumentStatus
{
    Uploaded = 1,
    Processing = 2,
    AwaitingValidation = 3,
    Validated = 4,
    Archived = 5,
    Rejected = 6,
    ProcessingFailed = 7
}
