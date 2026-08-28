using System.Security.Cryptography;
using System.Text;
using TixeNex.Api.Domain;
using TixeNex.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace TixeNex.Api.Infrastructure.Services;

public sealed class RefreshTokenService
{
    private static readonly SemaphoreSlim NonRelationalConsumptionGate =
        new(1, 1);

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

    public async Task<ApplicationUser?> TryConsumeAsync(
        string rawToken,
        DateTime consumedAtUtc,
        CancellationToken ct = default)
    {
        var hash = ComputeSha256(rawToken);

        if (!_db.Database.IsRelational())
        {
            await NonRelationalConsumptionGate.WaitAsync(ct);

            try
            {
                var refreshToken = await _db.RefreshTokens
                    .Include(token => token.User)
                    .FirstOrDefaultAsync(
                        token =>
                            token.TokenHash == hash &&
                            token.RevokedAtUtc == null &&
                            token.ExpiresAtUtc > consumedAtUtc &&
                            token.User != null &&
                            token.User.IsActive,
                        ct);

                if (refreshToken?.User is null)
                    return null;

                refreshToken.RevokedAtUtc = consumedAtUtc;
                await _db.SaveChangesAsync(ct);

                return refreshToken.User;
            }
            finally
            {
                NonRelationalConsumptionGate.Release();
            }
        }

        var candidate = await _db.RefreshTokens
            .AsNoTracking()
            .Where(token =>
                token.TokenHash == hash &&
                token.RevokedAtUtc == null &&
                token.ExpiresAtUtc > consumedAtUtc &&
                token.User != null &&
                token.User.IsActive)
            .Select(token => new
            {
                token.Id,
                token.UserId
            })
            .SingleOrDefaultAsync(ct);

        if (candidate is null)
            return null;

        var consumedRows = await _db.RefreshTokens
            .Where(token =>
                token.Id == candidate.Id &&
                token.RevokedAtUtc == null &&
                token.ExpiresAtUtc > consumedAtUtc)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    token => token.RevokedAtUtc,
                    consumedAtUtc),
                ct);

        if (consumedRows != 1)
            return null;

        return await _db.Users.SingleOrDefaultAsync(
            user => user.Id == candidate.UserId && user.IsActive,
            ct);
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
