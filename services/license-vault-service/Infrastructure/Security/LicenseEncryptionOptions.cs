
namespace LicenseVault.Api.Infrastructure.Security;

public sealed class LicenseEncryptionOptions
{
    public string EncryptionKey { get; set; } = string.Empty;

    public string FingerprintKey { get; set; } = string.Empty;
}
