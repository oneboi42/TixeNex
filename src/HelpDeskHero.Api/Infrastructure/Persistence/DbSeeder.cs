using HelpDeskHero.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDeskHero.Api.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await db.Database.MigrateAsync(ct);

        string[] roles = ["Admin", "Agent", "User"];

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        await CreateUserIfMissingAsync(userManager, "admin", "admin@helpdeskhero.local", "System Admin", "Admin1234", ["Admin", "Agent"]);
        await CreateUserIfMissingAsync(userManager, "agent", "agent@helpdeskhero.local", "Support Agent", "Agent1234", ["Agent"]);
        await CreateUserIfMissingAsync(userManager, "user", "user@helpdeskhero.local", "Demo User", "User1234", ["User"]);
    }

    private static async Task CreateUserIfMissingAsync(
        UserManager<ApplicationUser> userManager,
        string userName,
        string email,
        string displayName,
        string password,
        string[] roles)
    {
        var user = await userManager.FindByNameAsync(userName);
        if (user is not null)
            return;

        user = new ApplicationUser
        {
            UserName = userName,
            Email = email,
            DisplayName = displayName,
            IsActive = true,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(x => $"{x.Code}: {x.Description}"));
            throw new InvalidOperationException($"Cannot create seed user '{userName}'. {errors}");
        }

        var roleResult = await userManager.AddToRolesAsync(user, roles);
        if (!roleResult.Succeeded)
        {
            var errors = string.Join("; ", roleResult.Errors.Select(x => $"{x.Code}: {x.Description}"));
            throw new InvalidOperationException($"Cannot assign roles to seed user '{userName}'. {errors}");
        }
    }
}
