using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using MSLX.SDK.Models.Settings;

namespace MSLX.Tests;

public class SslSettingsTests
{
    [Fact]
    public void PrivateKeyValidationAcceptsRsaAndEcdsaPem()
    {
        using var rsa = RSA.Create(2048);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.True(CertificateHelper.IsValidPrivateKey(rsa.ExportRSAPrivateKeyPem(), out _));
        Assert.True(CertificateHelper.IsValidPrivateKey(ecdsa.ExportECPrivateKeyPem(), out _));
        Assert.True(CertificateHelper.IsValidPrivateKey(ecdsa.ExportPkcs8PrivateKeyPem(), out _));
    }

    [Fact]
    public void RequestRejectsPrivateKeyWithoutCertificate()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new UpdateSslSettingsRequest { PrivateKey = ecdsa.ExportECPrivateKeyPem() };

        var error = Assert.Single(request.Validate(new ValidationContext(request)));
        Assert.Contains(nameof(request.Certificate), error.MemberNames);
    }
}
