using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace FeatureFlare.Api.Auth;

public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string KeyTypeClaim = "featureflare:key-type";
    public const string AdminKeyType = "admin";
    public const string SdkKeyType = "sdk";
}

public static class Policies
{
    /// <summary>Management API: create/update/delete flags, segments and experiments.</summary>
    public const string Admin = "admin";

    /// <summary>Read-only SDK access: config download, streaming, evaluation and event ingestion.</summary>
    public const string Sdk = "sdk";
}

public sealed class ApiKeyOptions : AuthenticationSchemeOptions
{
    public List<string> AdminKeys { get; set; } = [];

    public List<string> SdkKeys { get; set; } = [];
}

/// <summary>
/// Accepts <c>Authorization: Bearer &lt;key&gt;</c> or <c>X-Api-Key: &lt;key&gt;</c>. Two key types exist so that
/// SDK keys, which ship inside services, can never modify configuration.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<ApiKeyOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = ReadKey();
        if (presented is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string? keyType = null;
        if (Matches(presented, Options.AdminKeys))
        {
            keyType = ApiKeyDefaults.AdminKeyType;
        }
        else if (Matches(presented, Options.SdkKeys))
        {
            keyType = ApiKeyDefaults.SdkKeyType;
        }

        if (keyType is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, $"{keyType}-key"),
                new Claim(ApiKeyDefaults.KeyTypeClaim, keyType),
            },
            Scheme.Name);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private string? ReadKey()
    {
        if (Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var headerKey) &&
            !string.IsNullOrWhiteSpace(headerKey))
        {
            return headerKey.ToString().Trim();
        }

        var authorization = Request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        return authorization.StartsWith(bearer, StringComparison.OrdinalIgnoreCase)
            ? authorization[bearer.Length..].Trim()
            : null;
    }

    /// <summary>Constant-time comparison to avoid leaking key prefixes through timing.</summary>
    private static bool Matches(string presented, IEnumerable<string> configured)
    {
        var presentedBytes = Encoding.UTF8.GetBytes(presented);
        var match = false;
        foreach (var key in configured)
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            match |= CryptographicOperations.FixedTimeEquals(presentedBytes, Encoding.UTF8.GetBytes(key));
        }

        return match;
    }
}
