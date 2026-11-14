namespace Qwader.Domain.Files;

public enum FilePurpose
{
    ProductImage = 1,
    TransferProof = 2,
    VoucherAttachment = 3,
    ExpenseAttachment = 4,
    /// <summary>A financial account's logo (the wallet's, the bank's), shown on its payment buttons.</summary>
    AccountLogo = 5,
}

/// <summary>Metadata of an uploaded file. The bytes live outside wwwroot under a random name.</summary>
public class StoredFile
{
    public Guid Id { get; set; }
    public FilePurpose Purpose { get; set; }
    public string OriginalName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public int? UploadedById { get; set; }
}
