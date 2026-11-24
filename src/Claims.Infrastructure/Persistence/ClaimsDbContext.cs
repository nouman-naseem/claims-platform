using Claims.Application.Abstractions;
using Claims.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Claims.Infrastructure.Persistence;

public class ClaimsDbContext : DbContext, IClaimsDbContext
{
    public ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Claimant> Claimants => Set<Claimant>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<Decision> Decisions => Set<Decision>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.PasswordHash).HasMaxLength(500);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Market).HasConversion<string>().HasMaxLength(30);
        });

        mb.Entity<Claimant>(e =>
        {
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Market).HasConversion<string>().HasMaxLength(30);
            e.HasOne<User>().WithOne().HasForeignKey<Claimant>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Policy>(e =>
        {
            e.HasIndex(x => x.PolicyNumber).IsUnique();
            e.HasIndex(x => x.HolderEmail);
            e.Property(x => x.PolicyNumber).HasMaxLength(50);
            e.Property(x => x.HolderEmail).HasMaxLength(256);
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Market).HasConversion<string>().HasMaxLength(30);
            e.HasOne<Claimant>().WithMany().HasForeignKey(x => x.ClaimantId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Claim>(e =>
        {
            e.HasIndex(x => x.ClaimNumber).IsUnique();
            e.HasIndex(x => new { x.Status, x.SubmittedAt });
            e.HasIndex(x => x.AssignedOfficerId);
            e.HasIndex(x => x.ClaimantId);
            e.Property(x => x.ClaimNumber).HasMaxLength(40);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.IncidentLocation).HasMaxLength(300);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Market).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Ignore(x => x.DomainEvents);
            e.Ignore(x => x.IsDecided);

            e.HasOne(x => x.Policy).WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Claimant).WithMany().HasForeignKey(x => x.ClaimantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AssignedOfficer).WithMany().HasForeignKey(x => x.AssignedOfficerId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Documents).WithOne().HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Assessments).WithOne().HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Assignments).WithOne().HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.InformationRequests).WithOne().HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Decision).WithOne().HasForeignKey<Decision>(x => x.ClaimId).OnDelete(DeleteBehavior.Cascade);

            e.Navigation(x => x.Documents).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(x => x.Assessments).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(x => x.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(x => x.InformationRequests).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(x => x.Events).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        mb.Entity<ClaimDocument>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(200);
            e.Property(x => x.StorageKey).HasMaxLength(200);
            e.Property(x => x.ContentType).HasMaxLength(150);
        });
        mb.Entity<Assessment>(e => e.Property(x => x.Notes).HasMaxLength(4000));
        mb.Entity<Assignment>(e => e.HasIndex(x => x.OfficerId));
        mb.Entity<InformationRequest>(e =>
        {
            e.Property(x => x.Message).HasMaxLength(2000);
            e.Property(x => x.Response).HasMaxLength(4000);
            e.Ignore(x => x.IsOpen);
        });
        mb.Entity<Decision>(e =>
        {
            e.HasIndex(x => x.ClaimId).IsUnique();
            e.HasIndex(x => x.DecidedByUserId);
            e.Property(x => x.Reason).HasMaxLength(4000);
            e.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(30);
        });
        mb.Entity<ClaimEvent>(e =>
        {
            e.HasIndex(x => new { x.ClaimId, x.OccurredAt });
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(50);
        });

        mb.Entity<OutboxMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProcessedAt, x.OccurredAt });
            e.Property(x => x.Type).HasMaxLength(100);
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.LastError).HasMaxLength(2000);
        });

        // Ids are assigned in the domain. Without ValueGeneratedNever, EF treats a child that already has a
        // Guid key and is discovered through a tracked parent as existing (UPDATE) instead of new (INSERT).
        foreach (var entityType in mb.Model.GetEntityTypes())
        {
            var key = entityType.FindPrimaryKey();
            if (key is { Properties.Count: 1 } && key.Properties[0].ClrType == typeof(Guid))
                key.Properties[0].ValueGenerated = ValueGenerated.Never;
        }
    }

    // Transactional outbox: domain events raised by aggregates become outbox rows inside the same
    // SaveChanges (one database transaction) as the state change that produced them.
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CollectOutboxMessages();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CollectOutboxMessages();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void CollectOutboxMessages()
    {
        var claims = ChangeTracker.Entries<Claim>().Select(x => x.Entity).Where(c => c.DomainEvents.Count > 0).ToList();
        foreach (var claim in claims)
        {
            foreach (var domainEvent in claim.DomainEvents) OutboxMessages.Add(OutboxMessage.From(domainEvent));
            claim.ClearDomainEvents();
        }
    }
}
