using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using NetSparkleUpdater;
using NetSparkleUpdater.Configurations;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Interfaces;
using NetSparkleUpdater.SignatureVerifiers;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace MSLX.Desktop.Utils
{
    /// <summary>
    /// 管理 macOS .app 的 NetSparkle 更新生命周期。
    /// </summary>
    internal sealed class MacAppUpdateService : IDisposable
    {
        private const string AppCastUrlMetadataName = "NetSparkleAppCastUrl";
        private const string PublicKeyMetadataName = "NetSparkleEd25519PublicKey";

        private SparkleUpdater? _updater;

        /// <summary>
        /// 在 macOS .app 中启动更新检查。
        /// </summary>
        public async Task StartAsync()
        {
            if (!PlatformHelper.IsMacAppBundle() || _updater != null)
            {
                return;
            }

            string? appCastUrl = GetAssemblyMetadata(AppCastUrlMetadataName);
            string? publicKey = GetAssemblyMetadata(PublicKeyMetadataName);
            if (!IsValidHttpsUrl(appCastUrl) || string.IsNullOrWhiteSpace(publicKey))
            {
                Debug.WriteLine("[NetSparkle] 配置错误，跳过更新检查。");
                return;
            }

            string validatedAppCastUrl = appCastUrl!;

            try
            {
                using var iconStream = AssetLoader.Open(new Uri("avares://MSLX-Desktop/Assets/icon.ico"));
                var updater = new MacAppSparkleUpdater(
                    validatedAppCastUrl,
                    new Ed25519Checker(SecurityMode.Strict, publicKey))
                {
                    Configuration = CreateUpdateConfiguration(),
                    UIFactory = new MacAppUpdateUIFactory(new WindowIcon(iconStream)),
                    TmpDownloadFileNameWithExtension = $"MSLX-Update-{Guid.NewGuid():N}.dmg",
                    RelaunchAfterUpdate = false,
                    LogWriter = new LogWriter(LogWriterOutputMode.Trace)
                };

                updater.CloseApplicationAsync += CloseApplicationForUpdateAsync;
                _updater = updater;

                // 首次运行会检查更新，之后由 NetSparkle 按默认的 24 小时间隔控制频率。
                await updater.StartLoop(doInitialCheck: true, forceInitialCheck: false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NetSparkle] 启动更新服务失败：{ex}");
                Dispose();
            }
        }

        private static JSONConfiguration CreateUpdateConfiguration()
        {
            string configurationDirectory = Path.Combine(
                ConfigService.GetAppDataPath(),
                "NetSparkleUpdater");
            Directory.CreateDirectory(configurationDirectory);

            return new JSONConfiguration(
                new DesktopAssemblyAccessor(),
                Path.Combine(configurationDirectory, "data.json"));
        }

        /// <summary>
        /// 响应用户操作，立即检查一次应用更新。
        /// </summary>
        public async Task CheckForUpdatesAsync()
        {
            if (!PlatformHelper.IsMacAppBundle())
            {
                return;
            }

            await StartAsync();
            if (_updater != null)
            {
                await _updater.CheckForUpdatesAtUserRequest(ignoreSkippedVersions: true);
            }
        }

        private static async Task CloseApplicationForUpdateAsync()
        {
            // 自动安装程序就绪后先结束内置 Daemon，避免替换 App 时仍占用旧文件。
            await DaemonManager.StopRunningDaemon();
            await Dispatcher.UIThread.InvokeAsync(() => App.Instance?.ExitApplication());
        }

        private static string? GetAssemblyMetadata(string key)
        {
            return Assembly.GetExecutingAssembly()
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == key)
                ?.Value;
        }

        private static bool IsValidHttpsUrl(string? value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
                   uri.Scheme == Uri.UriSchemeHttps;
        }

        /// <summary>
        /// 直接从当前 Desktop 程序集读取 NetSparkle 所需的产品信息。
        /// </summary>
        private sealed class DesktopAssemblyAccessor : IAssemblyAccessor
        {
            private static readonly Assembly DesktopAssembly = typeof(App).Assembly;

            public string AssemblyCompany =>
                DesktopAssembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "MSLTeam";

            public string AssemblyCopyright =>
                DesktopAssembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

            public string AssemblyDescription =>
                DesktopAssembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? "MSLX Desktop";

            public string AssemblyTitle =>
                DesktopAssembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? "MSLX";

            public string AssemblyProduct =>
                DesktopAssembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "MSLX";

            public string AssemblyVersion =>
                DesktopAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
                DesktopAssembly.GetName().Version?.ToString() ??
                "0.0.0";
        }

        public void Dispose()
        {
            SparkleUpdater? updater = _updater;
            _updater = null;
            if (updater == null)
            {
                return;
            }

            updater.CloseApplicationAsync -= CloseApplicationForUpdateAsync;
            updater.Dispose();
        }
    }
}
