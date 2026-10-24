using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BotWatch.Gateway.Infrastructure;

// пользователи админки и ключи Data Protection: без ключей в базе cookie слетают при каждом рестарте пода.
// ролей нет, все пользователи равны, поэтому IdentityUserContext, а не IdentityDbContext
public class GatewayDbContext(DbContextOptions<GatewayDbContext> options)
    : IdentityUserContext<IdentityUser>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // по умолчанию таблицы AspNetUsers и т.п., а у нас везде snake_case
        builder.Entity<IdentityUser>().ToTable("users");
        builder.Entity<IdentityUserClaim<string>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<string>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<string>>().ToTable("user_tokens");
        builder.Entity<DataProtectionKey>().ToTable("data_protection_keys");
    }
}

internal class GatewayDbContextFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    public GatewayDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<GatewayDbContext>()
            .UseNpgsql("Host=localhost;Database=gateway")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new GatewayDbContext(options);
    }
}
