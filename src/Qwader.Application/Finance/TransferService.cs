using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Customers;
using Qwader.Application.Notifications;
using Qwader.Domain.Administration;
using Qwader.Domain.Files;
using Qwader.Domain.Finance;
using Qwader.Domain.Identity;
using Qwader.Domain.Notifications;

namespace Qwader.Application.Finance;

public interface ITransferService
{
    Task<PagedResult<IncomingTransferDto>> ListAsync(TransferListRequest request, CancellationToken ct = default);
    Task<IncomingTransferDto> GetAsync(int id, CancellationToken ct = default);
    Task<IncomingTransferDto> CreateAsync(CreateIncomingTransferRequest request, CancellationToken ct = default);
    Task<IncomingTransferDto> ConfirmAsync(int id, CancellationToken ct = default);
    Task<IncomingTransferDto> RejectAsync(int id, RejectTransferRequest request, CancellationToken ct = default);

    /// <summary>
    /// Records a transfer taken at the till (inside the sale's transaction). A pending one waits for review and stays as debt;
    /// a confirmed one is only a record — the money already went in as a sale payment.
    /// </summary>
    Task<IncomingTransfer> AddForSaleAsync(int saleInvoiceId, int? customerId, string senderName, string? phone, int accountId,
        decimal amount, string? reference, bool confirmed, CancellationToken ct = default);
}

public sealed class TransferService(
    IAppDbContext db,
    IVoucherService vouchers,
    IDocumentNumberGenerator numbers,
    IIdempotencyService idempotency,
    ICurrentUser currentUser,
    IClock clock,
    IAuditService audit,
    IPushNotifier push) : ITransferService
{
    private const string CreateOperation = "Transfer.Create";

    public async Task<PagedResult<IncomingTransferDto>> ListAsync(TransferListRequest request, CancellationToken ct = default)
    {
        var query = db.IncomingTransfers.AsNoTracking();
        if (request.Status is { } status) query = query.Where(t => t.Status == status);
        if (request.AccountId is { } accountId) query = query.Where(t => t.FinancialAccountId == accountId);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var raw = request.Search.Trim();
            var term = ArabicText.Normalize(raw);
            var phone = PhoneNumbers.Normalize(raw);
            query = query.Where(t => t.NormalizedSenderName.Contains(term) || t.ReferenceNo == raw || t.Number.EndsWith(raw)
                || (phone != null && t.SenderPhone!.Contains(phone)));
        }
        // Pending first (oldest waiting longest on top), then the most recent decisions.
        return await Project(query.OrderBy(t => t.Status == TransferStatus.Pending ? 0 : 1)
                .ThenBy(t => t.Status == TransferStatus.Pending ? t.Id : -t.Id))
            .ToPagedAsync(request, ct);
    }

    public async Task<IncomingTransferDto> GetAsync(int id, CancellationToken ct = default) =>
        await Project(db.IncomingTransfers.AsNoTracking().Where(t => t.Id == id)).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("الحوالة غير موجودة");

    public async Task<IncomingTransferDto> CreateAsync(CreateIncomingTransferRequest request, CancellationToken ct = default)
    {
        if (await idempotency.FindAsync(request.IdempotencyKey, CreateOperation, ct) is { } done) return await GetAsync(int.Parse(done.ResultId!), ct);

        if (!await db.FinancialAccounts.AnyAsync(a => a.Id == request.FinancialAccountId && a.IsActive, ct))
            throw new ValidationFailedException("financialAccountId", "الحساب المستلم غير موجود أو معطّل");
        if (request.ProofFileId is { } fileId && !await db.StoredFiles.AnyAsync(f => f.Id == fileId && f.Purpose == FilePurpose.TransferProof, ct))
            throw new ValidationFailedException("proofFileId", "صورة الإثبات غير موجودة، أعد رفعها");
        if (request.CustomerId is { } customerId && !await db.Customers.AnyAsync(c => c.Id == customerId && !c.IsWalkIn, ct))
            throw new ValidationFailedException("customerId", "العميل غير موجود");
        var reference = Clean(request.ReferenceNo);
        await EnsureReferenceUnusedAsync(request.FinancialAccountId, reference, null, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var transfer = new IncomingTransfer
        {
            Number = await numbers.NextAsync(DocumentType.IncomingTransfer, ct),
            SenderName = request.SenderName.Trim(),
            NormalizedSenderName = ArabicText.Normalize(request.SenderName),
            SenderPhone = PhoneNumbers.Normalize(request.SenderPhone),
            Amount = request.Amount,
            Channel = Clean(request.Channel),
            FinancialAccountId = request.FinancialAccountId,
            ReferenceNo = reference,
            TransferDate = DocumentDates.ToUtc(request.TransferDate, clock, "transferDate"),
            ProofFileId = request.ProofFileId,
            CustomerId = request.CustomerId,
            Notes = Clean(request.Notes),
        };
        db.IncomingTransfers.Add(transfer);
        await db.SaveChangesAsync(ct);
        idempotency.Record(request.IdempotencyKey, CreateOperation, "IncomingTransfer", transfer.Id.ToString());
        audit.Log("Transfer.Create", "IncomingTransfer", transfer.Id.ToString(), null, new { transfer.Number, transfer.Amount, transfer.SenderName, transfer.ReferenceNo });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        push.Notify(PushTopics.Transfers, new PushMessage("حوالة جديدة بانتظار التحقق",
            $"{transfer.SenderName} — {PushMessage.Money(transfer.Amount)}", "/transfers"), Permissions.TransfersReview);
        return await GetAsync(transfer.Id, ct);
    }

    public async Task<IncomingTransfer> AddForSaleAsync(int saleInvoiceId, int? customerId, string senderName, string? phone, int accountId,
        decimal amount, string? reference, bool confirmed, CancellationToken ct = default)
    {
        reference = Clean(reference);
        await EnsureReferenceUnusedAsync(accountId, reference, null, ct);
        var transfer = new IncomingTransfer
        {
            Number = await numbers.NextAsync(DocumentType.IncomingTransfer, ct),
            SenderName = senderName,
            NormalizedSenderName = ArabicText.Normalize(senderName),
            SenderPhone = phone,
            Amount = amount,
            FinancialAccountId = accountId,
            ReferenceNo = reference,
            TransferDate = clock.UtcNow,
            CustomerId = customerId,
            SaleInvoiceId = saleInvoiceId,
            Notes = confirmed ? "حوالة عند البيع — اعتُبرت مؤكدة حسب الإعدادات" : "دفعة عند البيع بانتظار التحقق",
            Status = confirmed ? TransferStatus.Confirmed : TransferStatus.Pending,
            ReviewedById = confirmed ? currentUser.UserId : null,
            ReviewedAt = confirmed ? clock.UtcNow : null,
        };
        db.IncomingTransfers.Add(transfer);
        await db.SaveChangesAsync(ct);
        return transfer;
    }

    public async Task<IncomingTransferDto> ConfirmAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var transfer = await LockAsync(id, ct);
        if (transfer.Status != TransferStatus.Pending)
            throw new BusinessRuleException(transfer.Status == TransferStatus.Confirmed ? "الحوالة مؤكدة من قبل" : "الحوالة مرفوضة ولا يمكن تأكيدها");

        // Confirmation is the moment the money counts: a receipt voucher moves it into the account (and off the customer's debt).
        var voucher = await vouchers.PostReceiptAsync(new Domain.Finance.ReceiptVoucher
        {
            Date = clock.UtcNow,
            Purpose = transfer.CustomerId is null ? ReceiptPurpose.OtherIncome : ReceiptPurpose.DebtCollection,
            CustomerId = transfer.CustomerId,
            SaleInvoiceId = transfer.SaleInvoiceId,
            PayerName = transfer.SenderName,
            PayerPhone = transfer.SenderPhone,
            Amount = transfer.Amount,
            FinancialAccountId = transfer.FinancialAccountId,
            Reference = transfer.ReferenceNo,
            Notes = $"حوالة مؤكدة {transfer.Number}",
            AttachmentFileId = transfer.ProofFileId,
            IncomingTransferId = transfer.Id,
        }, ct);

        transfer.Status = TransferStatus.Confirmed;
        transfer.ReviewedById = currentUser.UserId;
        transfer.ReviewedAt = clock.UtcNow;
        transfer.ReceiptVoucherId = voucher.Id;
        audit.Log("Transfer.Confirm", "IncomingTransfer", id.ToString(), new { Status = "Pending" }, new { Status = "Confirmed", voucher.Number });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<IncomingTransferDto> RejectAsync(int id, RejectTransferRequest request, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var transfer = await LockAsync(id, ct);
        if (transfer.Status != TransferStatus.Pending)
            throw new BusinessRuleException(transfer.Status == TransferStatus.Confirmed ? "لا يمكن رفض حوالة مؤكدة" : "الحوالة مرفوضة من قبل");

        // Nothing was counted, so nothing is reversed; a till transfer simply stays as the customer's debt.
        transfer.Status = TransferStatus.Rejected;
        transfer.ReviewedById = currentUser.UserId;
        transfer.ReviewedAt = clock.UtcNow;
        transfer.RejectReason = request.Reason.Trim();
        audit.Log("Transfer.Reject", "IncomingTransfer", id.ToString(), new { Status = "Pending" }, new { Status = "Rejected" }, transfer.RejectReason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Row lock so two reviewers deciding at the same moment cannot both act.</summary>
    private async Task<IncomingTransfer> LockAsync(int id, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"SELECT Id FROM IncomingTransfers WITH (UPDLOCK, ROWLOCK) WHERE Id = {id}", ct);
        return await db.IncomingTransfers.FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("الحوالة غير موجودة");
    }

    private async Task EnsureReferenceUnusedAsync(int accountId, string? reference, int? exceptId, CancellationToken ct)
    {
        if (reference is null) return;
        var used = await db.IncomingTransfers.AsNoTracking()
            .Where(t => t.FinancialAccountId == accountId && t.ReferenceNo == reference && t.Status != TransferStatus.Rejected && t.Id != exceptId)
            .Select(t => t.Number).FirstOrDefaultAsync(ct);
        if (used is not null)
            throw new ValidationFailedException("referenceNo", $"رقم المرجع «{reference}» مسجّل من قبل في الحوالة {used}");
    }

    private IQueryable<IncomingTransferDto> Project(IQueryable<IncomingTransfer> query)
    {
        var users = db.Users;
        return query.Select(t => new IncomingTransferDto(t.Id, t.Number, t.SenderName, t.SenderPhone, t.Amount, t.Channel,
            t.FinancialAccountId, t.FinancialAccount.Name, t.ReferenceNo, t.TransferDate, t.ProofFileId, t.Status,
            t.CustomerId, t.Customer != null ? t.Customer.Name : null,
            t.SaleInvoiceId, db.SaleInvoices.Where(s => s.Id == t.SaleInvoiceId).Select(s => s.Number).FirstOrDefault(),
            t.Notes, users.Where(u => u.Id == t.CreatedById).Select(u => u.FullName).FirstOrDefault(), t.CreatedAt,
            users.Where(u => u.Id == t.ReviewedById).Select(u => u.FullName).FirstOrDefault(), t.ReviewedAt, t.RejectReason,
            t.ReceiptVoucherId, db.ReceiptVouchers.Where(v => v.Id == t.ReceiptVoucherId).Select(v => v.Number).FirstOrDefault()));
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
