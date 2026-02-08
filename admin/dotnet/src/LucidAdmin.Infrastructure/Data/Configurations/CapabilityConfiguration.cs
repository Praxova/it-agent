using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class CapabilityConfiguration : IEntityTypeConfiguration<Capability>
{
    public void Configure(EntityTypeBuilder<Capability> builder)
    {
        builder.HasKey(c => c.Id);

        // Unique constraint on CapabilityId (not the GUID Id)
        builder.Property(c => c.CapabilityId).IsRequired().HasMaxLength(100);
        builder.HasIndex(c => c.CapabilityId).IsUnique();

        builder.Property(c => c.Version).IsRequired().HasMaxLength(20);
        builder.Property(c => c.Category).IsRequired().HasMaxLength(50);
        builder.HasIndex(c => c.Category);

        builder.Property(c => c.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Description).HasMaxLength(1024);

        builder.Property(c => c.RequiredProvidersJson).HasColumnType("TEXT");
        builder.Property(c => c.DependenciesJson).HasColumnType("TEXT");
        builder.Property(c => c.MinToolServerVersion).HasMaxLength(20);

        builder.Property(c => c.ConfigurationSchema).HasColumnType("TEXT");
        builder.Property(c => c.ConfigurationExample).HasColumnType("TEXT");

        builder.Property(c => c.DocumentationUrl).HasMaxLength(512);

        builder.HasMany(c => c.Mappings)
            .WithOne(m => m.Capability)
            .HasForeignKey(m => m.CapabilityId)
            .HasPrincipalKey(c => c.CapabilityId)
            .OnDelete(DeleteBehavior.Restrict);

        // Seed built-in capabilities
        SeedBuiltInCapabilities(builder);
    }

    private static void SeedBuiltInCapabilities(EntityTypeBuilder<Capability> builder)
    {
        var now = DateTime.UtcNow;

        builder.HasData(
            // Active Directory Capabilities
            new Capability
            {
                Id = new Guid("a1b2c3d4-0001-0000-0000-000000000001"),
                CapabilityId = "ad-password-reset",
                Version = "1.0.0",
                Category = "active-directory",
                DisplayName = "Reset user passwords in Active Directory",
                Description = "Reset Active Directory user passwords with validation",
                RequiresServiceAccount = true,
                RequiredProvidersJson = "[\"windows-ad\"]",
                IsBuiltIn = true,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new Capability
            {
                Id = new Guid("a1b2c3d4-0002-0000-0000-000000000001"),
                CapabilityId = "ad-group-add",
                Version = "1.0.0",
                Category = "active-directory",
                DisplayName = "Add user to Active Directory group",
                Description = "Add a user to an Active Directory security or distribution group",
                RequiresServiceAccount = true,
                RequiredProvidersJson = "[\"windows-ad\"]",
                IsBuiltIn = true,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new Capability
            {
                Id = new Guid("a1b2c3d4-0003-0000-0000-000000000001"),
                CapabilityId = "ad-group-remove",
                Version = "1.0.0",
                Category = "active-directory",
                DisplayName = "Remove user from Active Directory group",
                Description = "Remove a user from an Active Directory security or distribution group",
                RequiresServiceAccount = true,
                RequiredProvidersJson = "[\"windows-ad\"]",
                IsBuiltIn = true,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new Capability
            {
                Id = new Guid("a1b2c3d4-0004-0000-0000-000000000001"),
                CapabilityId = "ad-user-lookup",
                Version = "1.0.0",
                Category = "active-directory",
                DisplayName = "Look up user information in Active Directory",
                Description = "Query Active Directory for user information and attributes",
                RequiresServiceAccount = true,
                RequiredProvidersJson = "[\"windows-ad\"]",
                IsBuiltIn = true,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new Capability
            {
                Id = new Guid("a1b2c3d4-0005-0000-0000-000000000001"),
                CapabilityId = "ad-user-unlock",
                Version = "1.0.0",
                Category = "active-directory",
                DisplayName = "Unlock a locked Active Directory account",
                Description = "Unlock a locked Active Directory user account",
                RequiresServiceAccount = true,
                RequiredProvidersJson = "[\"windows-ad\"]",
                IsBuiltIn = true,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            // File System Capabilities
            new Capability
            {
                Id = new Guid("a1b2c3d4-0006-0000-0000-000000000001"),
                CapabilityId = "ntfs-permission-grant",
                Version = "1.0.0",
                Category = "file-system",
                DisplayName = "Grant NTFS file/folder permissions",
                Description = "Grant NTFS permissions to users or groups for files and folders",
                RequiresServiceAccount = true,
                RequiredProvidersJson = "[\"windows-ad\"]",
                IsBuiltIn = true,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new Capability
            {
                Id = new Guid("a1b2c3d4-0007-0000-0000-000000000001"),
                CapabilityId = "ntfs-permission-revoke",
                Version = "1.0.0",
                Category = "file-system",
                DisplayName = "Revoke NTFS file/folder permissions",
                Description = "Revoke NTFS permissions from users or groups for files and folders",
                RequiresServiceAccount = true,
                RequiredProvidersJson = "[\"windows-ad\"]",
                IsBuiltIn = true,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            // Azure Capabilities
            new Capability
            {
                Id = new Guid("a1b2c3d4-0008-0000-0000-000000000001"),
                CapabilityId = "azure-user-lookup",
                Version = "1.0.0",
                Category = "azure",
                DisplayName = "Look up user in Azure AD / Entra ID",
                Description = "Query Microsoft Entra ID for user details via Microsoft Graph API",
                RequiresServiceAccount = true,
                RequiredProvidersJson = "[\"azure\"]",
                IsBuiltIn = true,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new Capability
            {
                Id = new Guid("a1b2c3d4-0009-0000-0000-000000000001"),
                CapabilityId = "azure-vm-lookup",
                Version = "1.0.0",
                Category = "azure",
                DisplayName = "Look up virtual machine in Azure",
                Description = "Query Azure Resource Manager for VM details including status, size, and IPs",
                RequiresServiceAccount = true,
                RequiredProvidersJson = "[\"azure\"]",
                IsBuiltIn = true,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            // Workstation Management Capabilities
            new Capability
            {
                Id = new Guid("a1b2c3d4-0010-0000-0000-000000000001"),
                CapabilityId = "ad-computer-lookup",
                Version = "1.0.0",
                Category = "active-directory",
                DisplayName = "Look up user's assigned computer(s)",
                Description = "Query Active Directory for computer objects assigned to a user via managedBy attribute",
                RequiresServiceAccount = true,
                RequiredProvidersJson = "[\"windows-ad\"]",
                IsBuiltIn = true,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new Capability
            {
                Id = new Guid("a1b2c3d4-0011-0000-0000-000000000001"),
                CapabilityId = "remote-software-install",
                Version = "1.0.0",
                Category = "workstation-management",
                DisplayName = "Install software on remote computer",
                Description = "Install software packages on remote Windows computers via PowerShell Remoting (WinRM)",
                RequiresServiceAccount = true,
                RequiredProvidersJson = "[\"windows-ad\"]",
                IsBuiltIn = true,
                IsEnabled = true,
                CreatedAt = now,
                UpdatedAt = now
            }
        );
    }
}
