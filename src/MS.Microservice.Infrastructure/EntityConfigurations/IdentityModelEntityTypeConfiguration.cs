using MS.Microservice.Domain.Aggregates.IdentityModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MS.Microservice.Infrastructure.EntityConfigurations
{
    public class IdentityUserEntityTypeConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.ToTable("Users");
            builder.Ignore(p => p.DomainEvents);

            builder.Property(p => p.Account)
                .HasField("_account")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .IsRequired()
                .HasMaxLength(25);
            builder.Property(p => p.Name)
                .HasField("_name")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .IsRequired()
                .HasMaxLength(25);
            builder.Property(p => p.CreatorId)
                .HasField("_creatorId")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .IsRequired();
            builder.Property(p => p.Email)
                .HasField("_email")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .IsRequired(false)
                .HasMaxLength(200);
            builder.Property(p => p.Password)
                .HasField("_password")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .IsRequired()
                .HasMaxLength(250);
            builder.Property(p => p.Salt)
                .HasField("_salt")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .IsRequired()
                .HasMaxLength(4);
            builder.Property(p => p.Telephone)
                .HasField("_telephone")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .IsRequired(false)
                .HasMaxLength(20);
            builder.Property(p => p.FzAccount)
                .HasField("_fzAccount")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .HasMaxLength(50);
            builder.Property(p => p.FzId)
                .HasField("_fzId")
                .UsePropertyAccessMode(PropertyAccessMode.Field)
                .HasMaxLength(50);

            #region RelationShip
            builder.HasMany(p => p.Roles)
                .WithMany(p => p.Users)
                .UsingEntity<UserRole>(
                    right => right.HasOne(ur => ur.Role)
                            .WithMany()
                            .HasForeignKey(ur => ur.RoleId)
                            .OnDelete(DeleteBehavior.Cascade),
                    left => left.HasOne(ur => ur.User)
                            .WithMany()
                            .HasForeignKey(ur => ur.UserId)
                            .OnDelete(DeleteBehavior.Cascade),
                    joinTable => {
                        joinTable.ToTable("UserRoles");    
                        joinTable.HasKey(j => new { j.UserId, j.RoleId });
                        joinTable.HasQueryFilter(p => p.User.DeletedAt == null);
                    }
                );
            #endregion

            builder.HasIndex(p => p.Account).IsUnique();
            builder.HasIndex(p => p.FzAccount).IsUnique();
        }
    }

    public class IdentityRoleEntityTypeConfiguration : IEntityTypeConfiguration<Role>
    {
        public void Configure(EntityTypeBuilder<Role> builder)
        {
            builder.ToTable("Roles");
            builder.Ignore(p => p.DomainEvents);

            builder.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(25);
            builder.Property(p => p.Description)
                .HasMaxLength(25);

            builder.HasMany(p => p.Actions)
                .WithMany(p => p.Roles)
                .UsingEntity<RoleAction>(
                    right => right.HasOne(ra => ra.Action)
                            .WithMany()
                            .HasForeignKey(ra => ra.ActionId)
                            .OnDelete(DeleteBehavior.Cascade),
                    left => left.HasOne(ra => ra.Role)
                            .WithMany()
                            .HasForeignKey(ra=>ra.RoleId)
                            .OnDelete(DeleteBehavior.Cascade),
                    joinTable => {
                        joinTable.ToTable("RoleActions");
                        joinTable.HasKey(j => new { j.RoleId, j.ActionId });
                    }
                );
        }
    }

    public class IdentityActionEntityTypeConfiguration : IEntityTypeConfiguration<Action>
    {
        public void Configure(EntityTypeBuilder<Action> builder)
        {
            builder.ToTable("Actions");
            builder.Ignore(p => p.DomainEvents);

            builder.Property(p => p.Name)
                .IsRequired()
                .HasMaxLength(25);
            builder.Property(p => p.Path)
                .IsRequired()
                .HasMaxLength(200);
        }
    }
}
