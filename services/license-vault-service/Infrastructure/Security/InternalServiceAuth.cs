using System.Security.Cryptography;
using System.Text;

namespace LicenseVault.Api.Infrastructure.Security;

public sealed class InternalServiceAuth
{
    private readonly byte[] _expectedKey;

    public InternalServiceAuth(IConfiguration configuration)
    {
        var key = configuration["InternalService:Key"];

        if (string.IsNullOrWhiteSpace(key) ||
            key.Length < 32)
        {
            throw new InvalidOperationException(
                "Internal service key is missing or invalid");
        }

        _expectedKey = Encoding.UTF8.GetBytes(key);
    }

    public bool Validate(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(
                "X-Internal-Service-Key",
                out var values) ||
            values.Count != 1)
        {
            return false;
        }

        var received = values[0];

        if (string.IsNullOrWhiteSpace(received))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            _expectedKey,
            Encoding.UTF8.GetBytes(received));
    }
}
