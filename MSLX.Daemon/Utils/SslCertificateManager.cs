using Microsoft.AspNetCore.Mvc.Rendering;
using MSLX.Daemon.Utils.ConfigUtils;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MSLX.Daemon.Utils
{
    public static class SslCertificateManager
    {
        private static X509Certificate2? _cachedCert;
        private static readonly object _lock = new object();
        public static string? CurrentSubject { get { lock (_lock) return _cachedCert?.Subject; } }
        
        internal static X509Certificate2 LoadCertificate(string certificatePem, string privateKeyPem)
        {
            using var certificate = X509Certificate2.CreateFromPem(certificatePem, privateKeyPem);
            return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), null);
        }

        internal static void ReplaceCertificate(X509Certificate2 certificate)
        {
            lock (_lock)
            {
                var previous = _cachedCert;
                _cachedCert = certificate;
                previous?.Dispose();
            }
        }

        public static X509Certificate2? GetCertificate()
        {
            if (_cachedCert == null)
            {
                ReloadCertificate();
            }
            return _cachedCert;
        }
        
        public static void ReloadCertificate()
        {
            lock (_lock)
            {
                string certDir = Path.Combine(IConfigBase.GetAppConfigPath(), "certs");
                string pemPath = Path.Combine(certDir, "server.pem");
                string keyPath = Path.Combine(certDir, "server.key");

                bool loadSuccess = false;

                // 自定义证书
                if (File.Exists(pemPath) && File.Exists(keyPath))
                {
                    try
                    {
                        ReplaceCertificate(LoadCertificate(File.ReadAllText(pemPath), File.ReadAllText(keyPath)));
                        loadSuccess = true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SSL Manager] 重新加载自定义证书失败，将启用临时证书: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("[SSL Manager] 未找到自定义证书文件，将生成临时证书...");
                }

                // 临时证书
                if (!loadSuccess)
                {
                    ReplaceCertificate(GenerateFallbackCertificate());
                }
            }
        }
        
        private static X509Certificate2 GenerateFallbackCertificate()
        {
            using var rsa = RSA.Create(2048);
            var dnBuilder = new X500DistinguishedNameBuilder();
            
            dnBuilder.AddCommonName("MSLX Emergency Temporary Certificate");
            
            dnBuilder.AddOrganizationName("净善宫");
            dnBuilder.AddOrganizationalUnitName("纳西妲最可爱啦!");
            dnBuilder.AddLocalityName("Sumeru");
            dnBuilder.AddCountryOrRegion("CN");
            var req = new CertificateRequest(
                dnBuilder.Build(),
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            // 把本地回环签进去
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName("localhost");
            sanBuilder.AddIpAddress(IPAddress.Loopback);
            req.CertificateExtensions.Add(sanBuilder.Build());

            using var cert = req.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddMonths(1));
            
            return new X509Certificate2(cert.Export(X509ContentType.Pkcs12));
        }
    }
}
