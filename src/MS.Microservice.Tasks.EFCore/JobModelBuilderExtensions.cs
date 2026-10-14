using Microsoft.EntityFrameworkCore;

namespace MS.Microservice.Tasks.EFCore;

public static class JobModelBuilderExtensions
{
    /// <summary>Adds task-center tables to the host model; migrations and schema changes remain host-owned.</summary>
    public static ModelBuilder AddTaskCenter(this ModelBuilder model, string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var job = model.Entity<JobEntry>();
        job.ToTable("Jobs", schema);
        job.HasKey(x => new { x.ScopeHash, x.Id });
        job.Property(x => x.Id).ValueGeneratedNever();
        job.Property(x => x.ScopeHash).HasMaxLength(64).IsUnicode(false);
        job.Property(x => x.Scope).HasMaxLength(256);
        job.Property(x => x.HandlerName).HasMaxLength(128).IsUnicode(false);
        job.Property(x => x.Queue).HasMaxLength(64).IsUnicode(false);
        job.Property(x => x.ProgressMessage).HasMaxLength(1024);
        job.Property(x => x.ErrorCode).HasMaxLength(128);
        job.Property(x => x.ResultReference).HasMaxLength(4096);
        job.Property(x => x.Version).IsConcurrencyToken();
        job.HasIndex(x => new { x.Queue, x.State, x.NextRunAtUtcTicks, x.CreatedAtUtcTicks });
        job.HasIndex(x => new { x.ScopeHash, x.CreatedAtUtcTicks });

        var attempt = model.Entity<JobAttemptEntry>();
        attempt.ToTable("JobAttempts", schema);
        attempt.HasKey(x => new { x.ScopeHash, x.JobId, x.Number });
        attempt.Property(x => x.JobId).ValueGeneratedNever();
        attempt.Property(x => x.ScopeHash).HasMaxLength(64).IsUnicode(false);
        attempt.Property(x => x.ProgressMessage).HasMaxLength(1024);
        attempt.Property(x => x.ErrorCode).HasMaxLength(128);
        attempt.Property(x => x.ResultReference).HasMaxLength(4096);
        attempt.HasOne<JobEntry>().WithMany().HasForeignKey(x => new { x.ScopeHash, x.JobId })
            .OnDelete(DeleteBehavior.Cascade);
        return model;
    }
}
