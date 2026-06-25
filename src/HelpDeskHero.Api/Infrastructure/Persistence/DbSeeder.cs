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

        if (db.Database.IsRelational())
        {
            await db.Database.MigrateAsync(ct);
        }
        else
        {
            await db.Database.EnsureCreatedAsync(ct);
        }

        string[] roles = ["Admin", "Manager", "Agent", "User"];

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        await CreateUserIfMissingAsync(userManager, "admin", "admin@helpdeskhero.local", "System Admin", "Admin1234", ["Admin", "Agent"]);
        await CreateUserIfMissingAsync(userManager, "agent", "agent@helpdeskhero.local", "Support Agent", "Agent1234", ["Agent"]);
        await CreateUserIfMissingAsync(userManager, "agent1", "agent1@helpdeskhero.local", "Support Agent 1", "Agent1234", ["Agent"]);
        await CreateUserIfMissingAsync(userManager, "agent2", "agent2@helpdeskhero.local", "Support Agent 2", "Agent1234", ["Agent"]);
        await CreateUserIfMissingAsync(userManager, "user", "user@helpdeskhero.local", "Demo User", "User1234", ["User"]);

        await SeedSlaPoliciesAsync(db, ct);
    }

    private static async Task CreateUserIfMissingAsync(
        UserManager<ApplicationUser> userManager,
        string userName,
        string email,
        string displayName,
        string password,
        string[] roles)
    {
        var user = await userManager.FindByNameAsync(userName)
            ?? await userManager.FindByEmailAsync(email);

        if (user is not null)
        {
            await AddMissingRolesAsync(userManager, user, roles);
            return;
        }

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

    private static async Task AddMissingRolesAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser user,
        string[] roles)
    {
        var currentRoles = await userManager.GetRolesAsync(user);
        var missingRoles = roles
            .Where(role => !currentRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (missingRoles.Length == 0)
            return;

        var roleResult = await userManager.AddToRolesAsync(user, missingRoles);
        if (!roleResult.Succeeded)
        {
            var errors = string.Join("; ", roleResult.Errors.Select(x => $"{x.Code}: {x.Description}"));
            throw new InvalidOperationException($"Cannot assign roles to seed user '{user.UserName}'. {errors}");
        }
    }

    private static async Task SeedSlaPoliciesAsync(AppDbContext db, CancellationToken ct)
    {
        await AddSlaPolicyIfMissingAsync(db, "Low SLA", "Low", 240, 2880, ct);
        await AddSlaPolicyIfMissingAsync(db, "Medium SLA", "Medium", 60, 480, ct);
        await AddSlaPolicyIfMissingAsync(db, "High SLA", "High", 15, 120, ct);

        await db.SaveChangesAsync(ct);
    }

    private static async Task AddSlaPolicyIfMissingAsync(
        AppDbContext db,
        string name,
        string priority,
        int firstResponseMinutes,
        int resolveMinutes,
        CancellationToken ct)
    {
        var exists = await db.TicketSlaPolicies
            .AnyAsync(policy => policy.Priority == priority, ct);

        if (exists)
            return;

        db.TicketSlaPolicies.Add(new TicketSlaPolicy
        {
            Name = name,
            Priority = priority,
            FirstResponseMinutes = firstResponseMinutes,
            ResolveMinutes = resolveMinutes,
            IsActive = true
        });
    }
}
