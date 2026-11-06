using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qwader.Domain.Notifications;

namespace Qwader.Infrastructure.Persistence.Configurations;

public sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> b)
    {
        b.Property(n => n.Title).HasMaxLength(200).IsRequired();
        b.Property(n => n.Body).HasMaxLength(1000).IsRequired();
        b.Property(n => n.Url).HasMaxLength(300).IsRequired();
        b.HasIndex(n => new { n.UserId, n.Id });
        b.HasIndex(n => n.CreatedAt);
        b.HasOne<Domain.Identity.AppUser>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
