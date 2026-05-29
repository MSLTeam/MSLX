using System.Security.Cryptography.X509Certificates;
using MSLX.Daemon.Utils.ConfigUtils;

namespace MSLX.Daemon.Utils
{
    public static class SslCertificateManager
    {
        private static X509Certificate2? _cachedCert;
        private static readonly object _lock = new object();
        
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

                if (File.Exists(pemPath) && File.Exists(keyPath))
                {
                    try
                    {
                        using var pemCert = X509Certificate2.CreateFromPemFile(pemPath, keyPath);
                        
                        var newCert = new X509Certificate2(pemCert.Export(X509ContentType.Pkcs12));

                        var oldCert = _cachedCert;
                        _cachedCert = newCert;
                        
                        oldCert?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[SSL Manager] 重新加载证书失败: {ex.Message}");
                        throw;
                    }
                }
            }
        }
    }
}