using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qwader.Domain.Administration;

namespace Qwader.Infrastructure.Persistence.Configurations;

public sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> b)
    {
        b.HasKey(s => s.Key);
        b.Property(s => s.Key).HasMaxLength(100).IsUnicode(false);
        b.Property(s => s.Value).HasMaxLength(2000).IsRequired();
    }
}

public sealed class DocumentSequenceConfiguration : IEntityTypeConfiguration<DocumentSequence>
{
    public void Configure(EntityTypeBuilder<DocumentSequence> b)
    {
        b.HasKey(s => new { s.DocumentType, s.Year });
    }
}

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.HasKey(r => r.Key);
        b.Property(r => r.Key).ValueGeneratedNever();
        b.Property(r => r.Operation).HasMaxLength(100).IsUnicode(false).IsRequired();
        b.Property(r => r.ResultType).HasMaxLength(100).IsUnicode(false);
        b.Property(r => r.ResultId).HasMaxLength(50).IsUnicode(false);
        b.HasIndex(r => r.CreatedAt);
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(a => a.UserName).HasMaxLength(256);
        b.Property(a => a.Action).HasMaxLength(100).IsUnicode(false).IsRequired();
        b.Property(a => a.EntityType).HasMaxLength(100).IsUnicode(false).IsRequired();
        b.Property(a => a.EntityId).HasMaxLength(50).IsUnicode(false);
        b.Property(a => a.Reason).HasMaxLength(500);
        b.Property(a => a.IpAddress).HasMaxLength(45).IsUnicode(false);
        b.HasIndex(a => new { a.EntityType, a.EntityId });
        b.HasIndex(a => a.OccurredAt);
    }
}
