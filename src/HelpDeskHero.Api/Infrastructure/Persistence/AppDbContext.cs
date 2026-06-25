using HelpDeskHero.Api.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Infrastructure.Persistence;

public sealed class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();
    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>(b =>
        {
            b.ToTable("Users");

            b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            b.Property(x => x.IsActive).IsRequired();
            b.Property(x => x.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<Ticket>(b =>
        {
            b.ToTable("Tickets");
            b.HasKey(x => x.Id);

            b.Property(x => x.Number).HasMaxLength(30).IsRequired();
            b.Property(x => x.Title).HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasMaxLength(4000).IsRequired();
            b.Property(x => x.Status).HasMaxLength(30).IsRequired();
            b.Property(x => x.Priority).HasMaxLength(30).IsRequired();
            b.Property(x => x.CreatedAtUtc).IsRequired();

            b.Property(x => x.RowVersion).IsRowVersion();

            b.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.ToTable("RefreshTokens");
            b.HasKey(x => x.Id);

            b.Property(x => x.UserId).HasMaxLength(450).IsRequired();

            b.Property(x => x.TokenHash).HasMaxLength(256).IsRequired();
            b.HasIndex(x => x.TokenHash).IsUnique();

            b.Property(x => x.DeviceName).HasMaxLength(200).IsRequired();
            b.Property(x => x.IpAddress).HasMaxLength(64);

            b.Property(x => x.CreatedAtUtc).IsRequired();
            b.Property(x => x.ExpiresAtUtc).IsRequired();
            b.Property(x => x.RevokedAtUtc);

            b.Ignore(x => x.IsActive);

            b.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLog>(b =>
        {
            b.ToTable("AuditLogs");
            b.HasKey(x => x.Id);

            b.Property(x => x.CreatedAtUtc).IsRequired();

            b.Property(x => x.Action)
                .HasMaxLength(100)
                .IsRequired();

            b.Property(x => x.EntityName)
                .HasMaxLength(100)
                .IsRequired();

            b.Property(x => x.EntityId)
                .HasMaxLength(100)
                .IsRequired();

            b.Property(x => x.UserId)
                .HasMaxLength(450);

            b.Property(x => x.UserName)
                .HasMaxLength(256);

            b.Property(x => x.IpAddress)
                .HasMaxLength(64);
        });

        modelBuilder.Entity<UserNotification>(b =>
        {
            b.ToTable("UserNotifications");
            b.HasKey(x => x.Id);

            b.Property(x => x.UserId)
                .HasMaxLength(450);

            b.Property(x => x.Subject)
                .HasMaxLength(200)
                .IsRequired();

            b.Property(x => x.Body)
                .HasMaxLength(4000)
                .IsRequired();

            b.Property(x => x.IsRead).IsRequired();
            b.Property(x => x.CreatedAtUtc).IsRequired();
            b.Property(x => x.ReadAtUtc);

            b.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAtUtc });
        });

        modelBuilder.Entity<TicketComment>(b =>
        {
            b.ToTable("TicketComments");
            b.HasKey(x => x.Id);

            b.Property(x => x.Body)
                .HasMaxLength(4000)
                .IsRequired();

            b.Property(x => x.CreatedAtUtc).IsRequired();

            b.Property(x => x.CreatedByUserId)
                .HasMaxLength(450)
                .IsRequired();

            b.Property(x => x.CreatedByDisplayName)
                .HasMaxLength(200)
                .IsRequired();

            b.HasOne(x => x.Ticket)
                .WithMany()
                .HasForeignKey(x => x.TicketId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasQueryFilter(x => !x.Ticket.IsDeleted);

            b.HasIndex(x => new { x.TicketId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<TicketAttachment>(b =>
        {
            b.ToTable("TicketAttachments");
            b.HasKey(x => x.Id);

            b.Property(x => x.OriginalFileName)
                .HasMaxLength(260)
                .IsRequired();

            b.Property(x => x.StoredFileName)
                .HasMaxLength(260)
                .IsRequired();

            b.Property(x => x.RelativePath)
                .HasMaxLength(520)
                .IsRequired();

            b.Property(x => x.ContentType)
                .HasMaxLength(200)
                .IsRequired();

            b.Property(x => x.SizeBytes).IsRequired();
            b.Property(x => x.UploadedAtUtc).IsRequired();

            b.Property(x => x.UploadedByUserId)
                .HasMaxLength(450)
                .IsRequired();

            b.HasOne(x => x.Ticket)
                .WithMany()
                .HasForeignKey(x => x.TicketId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasQueryFilter(x => !x.Ticket.IsDeleted);

            b.HasIndex(x => new { x.TicketId, x.UploadedAtUtc });
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Ticket>())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
                entry.Entity.DeletedAtUtc = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
