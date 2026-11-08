using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
namespace Library.Api;

/// <summary>Случайные серверные токены стенда без клиентского назначения роли.</summary>
public sealed class TokenRegistry
{
    private readonly ConcurrentDictionary<string, ClaimsPrincipal> _tokens = new();
    public string Issue(string role, string actor)
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tokens[token] = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, actor), new Claim(ClaimTypes.Role, role) }, "LabToken"));
        return token;
    }
    public ClaimsPrincipal? Find(string token) => _tokens.GetValueOrDefault(token);
}
public sealed class TokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, TokenRegistry tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string value = Request.Headers.Authorization.ToString();
        var principal = value.StartsWith("Bearer ") ? tokens.Find(value[7..]) : null;
        return Task.FromResult(principal is null ? AuthenticateResult.NoResult() :
            AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
