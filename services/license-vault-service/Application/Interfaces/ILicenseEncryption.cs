
namespace LicenseVault.Api.Application.Interfaces;

public interface ILicenseEncryption
{
    string Encrypt(string plaintext);

    string Decrypt(string encrypted);

    string Fingerprint(string plaintext);
}
