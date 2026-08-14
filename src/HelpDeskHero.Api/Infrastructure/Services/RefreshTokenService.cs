using System.Security.Cryptography;
using System.Text;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Infrastructure.Services;

public sealed class RefreshTokenService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;

    public RefreshTokenService(AppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<(string rawToken, DateTime expiresAtUtc)>
        CreateAsync(
            string userId,
            string deviceName,
            string? ipAddress,
            DateTime? notAfterUtc = null,
            CancellationToken ct = default)
    {
        var rawToken = Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(64));

        var hash = ComputeSha256(rawToken);

        var days = int.Parse(
            _configuration["Jwt:RefreshTokenDays"] ?? "7");

        var now = DateTime.UtcNow;
        var expiresAtUtc = now.AddDays(days);

        if (notAfterUtc.HasValue &&
            notAfterUtc.Value < expiresAtUtc)
        {
            expiresAtUtc = notAfterUtc.Value;
        }

        if (expiresAtUtc <= now)
        {
            throw new InvalidOperationException(
                "Cannot create a refresh token for an expired session.");
        }

        var refresh = new RefreshToken
        {
            UserId = userId,
            TokenHash = hash,
            DeviceName = deviceName,
            IpAddress = ipAddress,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc
        };

        _db.RefreshTokens.Add(refresh);
        await _db.SaveChangesAsync(ct);

        return (rawToken, expiresAtUtc);
    }

    public async Task<RefreshToken?> GetActiveByRawTokenAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = ComputeSha256(rawToken);

        return await _db.RefreshTokens
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.TokenHash == hash && x.RevokedAtUtc == null, ct);
    }

    public async Task RevokeAsync(RefreshToken refreshToken, CancellationToken ct = default)
    {
        refreshToken.RevokedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static string ComputeSha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}