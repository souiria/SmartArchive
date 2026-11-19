namespace SmartArchive.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

public sealed class DuplicateDocumentException : DomainException
{
    public Guid ExistingDocumentId { get; }
    public string ExistingDocumentNumber { get; }

    public DuplicateDocumentException(Guid existingDocumentId, string existingDocumentNumber)
        : base($"A document with identical content already exists (Archive ID: {existingDocumentNumber}).")
    {
        ExistingDocumentId = existingDocumentId;
        ExistingDocumentNumber = existingDocumentNumber;
    }
}
