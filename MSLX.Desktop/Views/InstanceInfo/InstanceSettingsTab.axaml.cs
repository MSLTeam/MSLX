using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MSLX.Desktop.Models;
using MSLX.Desktop.Services;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Instance;
using Newtonsoft.Json.Linq;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views.InstanceInfo;

public partial class InstanceSettingsTab : UserControl
{
    private int _instanceId;
    private McServerInfo.ServerInfo? _original;
    private bool _initialized;
    private bool _isPathEditable;

    // 核心更新参数
    private string? _pendingCoreUrl;
    private string? _pendingCoreSha256;
    private string? _pendingCoreFileKey;

    // 绑定数据集合
    public ObservableCollection<PortMappingItem> PortList { get; } = new();
    public ObservableCollection<VolumeMappingItem> VolumeList { get; } = new();
    public ObservableCollection<EnvVarItem> EnvList { get; } = new();
    public ObservableCollection<HostMappingItem> HostList { get; } = new();
    public ObservableCollection<FrpTunnelItem> FrpList { get; } = new();

    // SignalR 监听服务
    private UpdateProgressSignalRService? _updateSignalR;

    public Action<string, bool>? OnSaveResult { get; set; }

    public InstanceSettingsTab()
    {
        InitializeComponent();

        PortItemsControl.ItemsSource = PortList;
        VolumeItemsControl.ItemsSource = VolumeList;
        EnvItemsControl.ItemsSource = EnvList;
        HostItemsControl.ItemsSource = HostList;
        FrpItemsControl.ItemsSource = FrpList;

        MinMUnitCombo.SelectedIndex = 0;
        MaxMUnitCombo.SelectedIndex = 0;
        InputEncodingCombo.SelectedIndex = 0;
        OutputEncodingCombo.SelectedIndex = 0;
        FileEncodingCombo.SelectedIndex = 0;
        BackupLocationCombo.SelectedIndex = 0;
        AuthSelectCombo.SelectedIndex = 0;

        // 内嵌版本库选择器回调绑定
        InlineCoreSelector.OnCoreSelected += (url, sha256, filename, coreName) =>
        {
            CoreBox.Text = filename;
            _pendingCoreUrl = url;
            _pendingCoreSha256 = sha256;
            _pendingCoreFileKey = null;
            InlineCoreSelector.IsVisible = false;
            ShowToast("核心已选择", $"已选择 {filename}，保存设置后将自动下载部署。", NotificationType.Success);
        };
    }

    #region 公开 API & 数据加载

    public async Task LoadAsync(int instanceId)
    {
        _instanceId = instanceId;
        _initialized = false;
        SettingsBusyArea.IsBusy = true;
        SaveBtn.IsEnabled = false;

        try
        {
            // 获取 Java 版本列表与 FRP 列表
            await Task.WhenAll(
                FetchJavaVersionListsAsync(),
                FetchFrpListAsync()
            );

            var (success, settings, msg) = await InstanceService.GetGeneralSettingsAsync(instanceId);
            if (!success || settings == null)
            {
                ShowToast("加载设置失败", msg ?? "未知错误", NotificationType.Error);
                return;
            }

            _original = settings;
            FillForm(settings);
            _initialized = true;
        }
        catch (Exception ex)
        {
            ShowToast("加载异常", ex.Message, NotificationType.Error);
        }
        finally
        {
            SettingsBusyArea.IsBusy = false;
            SaveBtn.IsEnabled = true;
        }
    }

    private async Task FetchJavaVersionListsAsync()
    {
        try
        {
            // 在线 Java 列表
            string os = PlatformHelper.GetOS() switch
            {
                PlatformHelper.TheOSPlatform.Windows => "windows",
                PlatformHelper.TheOSPlatform.Linux => "linux",
                PlatformHelper.TheOSPlatform.OSX => "mac",
                _ => "linux"
            };
            string arch = PlatformHelper.GetOSArch() switch
            {
                PlatformHelper.TheArchitecture.Arm64 => "arm64",
                _ => "x64"
            };

            var resOnline = await MSLAPIService.GetJsonDataAsync("/jdk", "data", new Dictionary<string, string>
            {
                { "os", os },
                { "arch", arch }
            });

            if (resOnline.Success && resOnline.Data is JArray onlineArray)
            {
                OnlineJavaCombo.Items.Clear();
                foreach (var item in onlineArray)
                {
                    string verStr = item.ToString();
                    OnlineJavaCombo.Items.Add(new ComboBoxItem { Content = $"Java {verStr} (在线)", Tag = verStr });
                }
            }

            // 本地 Java 列表
            var resLocal = await DaemonAPIService.GetJsonDataAsync("/api/java/list", "data");
            if (resLocal.Success && resLocal.Data is JArray localArray)
            {
                LocalJavaCombo.Items.Clear();
                var list = localArray.ToObject<List<JavaInfo>>();
                if (list != null)
                {
                    foreach (var java in list)
                    {
                        LocalJavaCombo.Items.Add(new ComboBoxItem
                        {
                            Content = $"Java {java.Version} ({java.Path})",
                            Tag = java.Path
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InstanceSettingsTab] Fetch Java error: {ex.Message}");
        }
    }

    private async Task FetchFrpListAsync()
    {
        try
        {
            JObject res = await DaemonAPIService.GetJsonContentAsync("/api/frp/list");
            if (res["data"] is JArray tunnels)
            {
                FrpList.Clear();
                foreach (JObject item in tunnels.Cast<JObject>())
                {
                    string idStr = item["id"]?.ToString() ?? "";
                    string name = item["name"]?.ToString() ?? "";
                    string configType = item["configType"]?.ToString() ?? "";

                    FrpList.Add(new FrpTunnelItem
                    {
                        Id = idStr,
                        Label = $"[ID: {idStr}] {name} ({configType})"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[InstanceSettingsTab] Fetch FRP error: {ex.Message}");
        }
    }

    #endregion

    #region 表单填充与读取

    private void FillForm(McServerInfo.ServerInfo s)
    {
        NameBox.Text = s.Name;
        BaseBox.Text = s.Base;
        IsPathEditable = false;

        _pendingCoreUrl = null;
        _pendingCoreSha256 = null;
        _pendingCoreFileKey = null;

        CoreBox.Text = s.Core;
        ArgsBox.Text = s.Args;
        StopCommandBox.Text = s.StopCommand;
        AllowOriginASCIIColorsToggle.IsChecked = s.AllowOriginASCIIColors;
        MonitorPlayersToggle.IsChecked = s.MonitorPlayers;
        AutoRestartToggle.IsChecked = s.AutoRestart;
        ForceAutoRestartToggle.IsChecked = s.ForceAutoRestart;
        ForceExitDelayBox.Value = s.ForceExitDelay > 0 ? s.ForceExitDelay : 10;
        IgnoreEulaToggle.IsChecked = s.IgnoreEula;
        RunOnStartupToggle.IsChecked = s.RunOnStartup;
        ForceJvmUTF8Toggle.IsChecked = s.ForceJvmUTF8;

        ExpireTimePicker.SelectedDate = s.ExpireTime;

        // 内存设置与单位
        if (s.MinM.HasValue && s.MinM > 0 && s.MinM % 1024 == 0)
        {
            SelectComboByTag(MinMUnitCombo, "GB");
            MinMBox.Value = (decimal)s.MinM.Value / 1024m;
        }
        else
        {
            SelectComboByTag(MinMUnitCombo, "MB");
            MinMBox.Value = s.MinM ?? 1024;
        }

        if (s.MaxM.HasValue && s.MaxM > 0 && s.MaxM % 1024 == 0)
        {
            SelectComboByTag(MaxMUnitCombo, "GB");
            MaxMBox.Value = (decimal)s.MaxM.Value / 1024m;
        }
        else
        {
            SelectComboByTag(MaxMUnitCombo, "MB");
            MaxMBox.Value = s.MaxM ?? 4096;
        }

        // 备份设置
        BackupMaxCountBox.Value = s.BackupMaxCount > 0 ? s.BackupMaxCount : 20;
        BackupDelayBox.Value = s.BackupDelay >= 0 ? s.BackupDelay : 10;
        if (s.BackupPath == "MSLX://Backup/Instance" || s.BackupPath == "MSLX://Backup/Data")
        {
            SelectComboByTag(BackupLocationCombo, s.BackupPath);
            CustomBackupPathBox.IsVisible = false;
        }
        else
        {
            SelectComboByTag(BackupLocationCombo, "custom");
            CustomBackupPathBox.IsVisible = true;
            CustomBackupPathBox.Text = s.BackupPath;
        }

        // 外置登录
        if (string.IsNullOrEmpty(s.YggdrasilApiAddr))
        {
            SelectComboByTag(AuthSelectCombo, "none");
            CustomAuthUrlBox.IsVisible = false;
        }
        else if (s.YggdrasilApiAddr == "https://skin.mslmc.net/api/yggdrasil" || s.YggdrasilApiAddr == "https://littleskin.cn/api/yggdrasil")
        {
            SelectComboByTag(AuthSelectCombo, s.YggdrasilApiAddr);
            CustomAuthUrlBox.IsVisible = false;
        }
        else
        {
            SelectComboByTag(AuthSelectCombo, "custom");
            CustomAuthUrlBox.IsVisible = true;
            CustomAuthUrlBox.Text = s.YggdrasilApiAddr;
        }

        // 编码
        SelectComboByTag(InputEncodingCombo, s.InputEncoding?.ToLower() ?? "utf-8");
        SelectComboByTag(OutputEncodingCombo, s.OutputEncoding?.ToLower() ?? "utf-8");
        SelectComboByTag(FileEncodingCombo, s.FileEncoding?.ToLower() ?? "utf-8");

        // 路径
        ServerPropertiesPathBox.Text = NormalizeRelativeInstancePath(s.ServerPropertiesPath, "server.properties");
        PluginsPathBox.Text = NormalizeRelativeInstancePath(s.PluginsPath, "plugins");
        ModsPathBox.Text = NormalizeRelativeInstancePath(s.ModsPath, "mods");
        WorldPathBox.Text = NormalizeRelativeInstancePath(s.WorldPath, "world");
        RegionPathBox.Text = NormalizeRelativeInstancePath(s.RegionPath, "region");

        // FRP 绑定
        string frpIdsStr = s.BindFrpId ?? "";
        BindFrpIdBox.Text = frpIdsStr;
        var boundIds = frpIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToHashSet();
        foreach (var item in FrpList)
        {
            item.IsSelected = boundIds.Contains(item.Id);
        }

        // 解析 Java 模式
        ParseJavaType(s);

        // 解析 Docker 属性
        ParseDockerSettings(s);
    }

    private void ParseJavaType(McServerInfo.ServerInfo s)
    {
        string java = s.Java ?? "";
        string args = s.Args ?? "";

        if (args.Contains("mcdreforged"))
        {
            if (java == "docker-java" || java == "docker-custom")
            {
                bool isPreset = string.IsNullOrEmpty(s.DockerImage) || s.DockerImage.StartsWith("MSLX://DockerImage/Java/");
                SelectComboByTag(JavaTypeCombo, isPreset ? "mcdr-docker-java" : "mcdr-docker-custom");
            }
            else
            {
                SelectComboByTag(JavaTypeCombo, "mcdr");
            }

            var match = Regex.Match(args, @"^\s*""?([^""]+?)""?\s+-m\s+mcdreforged");
            McdrPythonBox.Text = match.Success ? match.Groups[1].Value.Trim() : "python";
        }
        else if (java == "docker-java" || java == "docker-custom")
        {
            SelectComboByTag(JavaTypeCombo, java);
        }
        else if (java == "none")
        {
            SelectComboByTag(JavaTypeCombo, "none");
        }
        else if (java == "java")
        {
            SelectComboByTag(JavaTypeCombo, "env");
        }
        else if (java.StartsWith("MSLX://Java/"))
        {
            SelectComboByTag(JavaTypeCombo, "online");
            string ver = java.Replace("MSLX://Java/", "");
            SelectComboByTag(OnlineJavaCombo, ver);
        }
        else
        {
            bool inLocal = LocalJavaCombo.Items.Cast<ComboBoxItem>().Any(item => (string?)item.Tag == java);
            if (inLocal)
            {
                SelectComboByTag(JavaTypeCombo, "local");
                SelectComboByTag(LocalJavaCombo, java);
            }
            else
            {
                SelectComboByTag(JavaTypeCombo, "custom");
                CustomJavaPathBox.Text = java;
            }
        }

        UpdateModeVisibilities();
    }

    private void ParseDockerSettings(McServerInfo.ServerInfo s)
    {
        DockerImageBox.Text = s.DockerImage ?? "MSLX://DockerImage/Java/25";
        if (s.DockerImage?.StartsWith("MSLX://DockerImage/Java/") == true)
        {
            string ver = s.DockerImage.Replace("MSLX://DockerImage/Java/", "");
            SelectComboByTag(DockerJavaVersionCombo, ver);
        }
        else
        {
            SelectComboByTag(DockerJavaVersionCombo, "21");
        }

        // 工作目录
        if (s.DockerWorkingDir == "/mslx-data" || string.IsNullOrEmpty(s.DockerWorkingDir))
        {
            WorkDirDefaultRadio.IsChecked = true;
            DockerWorkingDirBox.IsVisible = false;
            DockerWorkingDirBox.Text = "/mslx-data";
        }
        else
        {
            WorkDirCustomRadio.IsChecked = true;
            DockerWorkingDirBox.IsVisible = true;
            DockerWorkingDirBox.Text = s.DockerWorkingDir;
        }

        DockerNetworkModeBox.Text = s.DockerNetworkMode ?? "bridge";
        DockerNetworkAliasBox.Text = s.DockerNetworkAlias ?? "";

        // 端口映射
        string ports = s.DockerPorts ?? "";
        if (ports == "0")
        {
            PortModeHostRadio.IsChecked = true;
            PortMappingContainer.IsVisible = false;
            PortList.Clear();
        }
        else
        {
            PortModeMappedRadio.IsChecked = true;
            PortMappingContainer.IsVisible = true;
            PortList.Clear();
            if (!string.IsNullOrWhiteSpace(ports))
            {
                foreach (var p in ports.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = p.Split(':');
                    string host = parts.Length > 0 ? parts[0] : "";
                    string rest = parts.Length > 1 ? parts[1] : "";
                    var cParts = rest.Split('/');
                    string container = cParts.Length > 0 ? cParts[0] : "";
                    string proto = cParts.Length > 1 ? cParts[1].ToLower() : "tcp";
                    PortList.Add(new PortMappingItem { Host = host, Container = container, Protocol = proto });
                }
            }
        }

        // Volume
        VolumeList.Clear();
        if (!string.IsNullOrWhiteSpace(s.DockerVolumes))
        {
            foreach (var v in s.DockerVolumes.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = v.Split(':');
                VolumeList.Add(new VolumeMappingItem
                {
                    Host = parts.Length > 0 ? parts[0] : "",
                    Container = parts.Length > 1 ? parts[1] : ""
                });
            }
        }

        // EnvVars
        EnvList.Clear();
        if (!string.IsNullOrWhiteSpace(s.DockerEnvVars))
        {
            foreach (var e in s.DockerEnvVars.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = e.Split('=');
                EnvList.Add(new EnvVarItem
                {
                    Key = parts.Length > 0 ? parts[0] : "",
                    Value = parts.Length > 1 ? parts[1] : ""
                });
            }
        }

        // Hosts
        HostList.Clear();
        if (!string.IsNullOrWhiteSpace(s.DockerExtraHosts))
        {
            foreach (var h in s.DockerExtraHosts.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = h.Split(':');
                HostList.Add(new HostMappingItem
                {
                    Domain = parts.Length > 0 ? parts[0] : "",
                    IP = parts.Length > 1 ? parts[1] : ""
                });
            }
        }

        DockerCpuPercentageBox.Value = s.DockerCpuPercentage;
        DockerCpuCoresBox.Text = s.DockerCpuCores ?? "";
        DockerMaxMemoryMbBox.Value = s.DockerMaxMemoryMb;
        DockerMaxSwapMbBox.Value = s.DockerMaxSwapMb;
        DockerMaxStorageBox.Text = s.DockerMaxStorage ?? "";
        DockerUploadRateBox.Text = s.DockerUploadRate ?? "";
        DockerDownloadRateBox.Text = s.DockerDownloadRate ?? "";
        DockerExtraArgsBox.Text = s.DockerExtraArgs ?? "";
    }

    private UpdateServerRequest BuildUpdateSettings()
    {
        string javaTypeTag = GetSelectedComboTag(JavaTypeCombo) ?? "online";

        int minMValue = (int)(MinMBox.Value ?? 1024);
        if (GetSelectedComboTag(MinMUnitCombo) == "GB") minMValue *= 1024;

        int maxMValue = (int)(MaxMBox.Value ?? 4096);
        if (GetSelectedComboTag(MaxMUnitCombo) == "GB") maxMValue *= 1024;

        string javaVal = "none";
        string dockerImg = DockerImageBox.Text ?? "MSLX://DockerImage/Java/25";

        if (javaTypeTag == "online")
        {
            string ver = GetSelectedComboTag(OnlineJavaCombo) ?? "";
            javaVal = string.IsNullOrEmpty(ver) ? "" : $"MSLX://Java/{ver}";
        }
        else if (javaTypeTag == "local")
        {
            javaVal = GetSelectedComboTag(LocalJavaCombo) ?? "";
        }
        else if (javaTypeTag == "custom")
        {
            javaVal = CustomJavaPathBox.Text ?? "";
        }
        else if (javaTypeTag == "env")
        {
            javaVal = "java";
        }
        else if (javaTypeTag == "docker-java")
        {
            javaVal = "docker-java";
            string dockerVer = GetSelectedComboTag(DockerJavaVersionCombo) ?? "21";
            dockerImg = $"MSLX://DockerImage/Java/{dockerVer}";
        }
        else if (javaTypeTag == "docker-custom")
        {
            javaVal = "docker-custom";
        }
        else if (javaTypeTag == "mcdr")
        {
            javaVal = "none";
        }
        else if (javaTypeTag == "mcdr-docker-java")
        {
            javaVal = "docker-custom";
            string dockerVer = GetSelectedComboTag(DockerJavaVersionCombo) ?? "21";
            dockerImg = $"MSLX://DockerImage/Java/{dockerVer}";
        }
        else if (javaTypeTag == "mcdr-docker-custom")
        {
            javaVal = "docker-custom";
        }
        else if (javaTypeTag == "none")
        {
            javaVal = "none";
        }

        string argsVal = ArgsBox.Text ?? "";
        if (IsMcdrMode(javaTypeTag))
        {
            string py = (McdrPythonBox.Text ?? "python").Trim();
            string quoted = py.Contains(' ') && !py.StartsWith("\"") ? $"\"{py}\"" : py;
            argsVal = $"{quoted} -m mcdreforged start";
        }

        // FRP Bind ID
        string bindFrpIdVal = "";
        if (FrpSelectModeRadio.IsChecked == true)
        {
            bindFrpIdVal = string.Join(",", FrpList.Where(x => x.IsSelected).Select(x => x.Id));
        }
        else
        {
            bindFrpIdVal = BindFrpIdBox.Text ?? "";
        }

        // 端口映射序列化
        string dockerPortsVal = "0";
        if (PortModeMappedRadio.IsChecked == true)
        {
            dockerPortsVal = string.Join(",", PortList
                .Where(p => !string.IsNullOrWhiteSpace(p.Host) && !string.IsNullOrWhiteSpace(p.Container))
                .Select(p => $"{p.Host.Trim()}:{p.Container.Trim()}/{p.Protocol}"));
        }

        // Volume 序列化
        string dockerVolumesVal = string.Join(",", VolumeList
            .Where(v => !string.IsNullOrWhiteSpace(v.Host) && !string.IsNullOrWhiteSpace(v.Container))
            .Select(v => $"{v.Host.Trim()}:{v.Container.Trim()}"));

        // EnvVars 序列化
        string dockerEnvVarsVal = string.Join(",", EnvList
            .Where(e => !string.IsNullOrWhiteSpace(e.Key))
            .Select(e => $"{e.Key.Trim()}={e.Value.Trim()}"));

        // Hosts 序列化
        string dockerExtraHostsVal = string.Join(",", HostList
            .Where(h => !string.IsNullOrWhiteSpace(h.Domain) && !string.IsNullOrWhiteSpace(h.IP))
            .Select(h => $"{h.Domain.Trim()}:{h.IP.Trim()}"));

        // 工作目录
        string workingDir = WorkDirCustomRadio.IsChecked == true ? (DockerWorkingDirBox.Text ?? "/mslx-data") : "/mslx-data";

        // Yggdrasil API
        string authTag = GetSelectedComboTag(AuthSelectCombo) ?? "none";
        string yggdrasil = authTag == "custom" ? (CustomAuthUrlBox.Text ?? "") : (authTag == "none" ? "" : authTag);

        // 备份路径
        string backupLocTag = GetSelectedComboTag(BackupLocationCombo) ?? "MSLX://Backup/Instance";
        string backupPathVal = backupLocTag == "custom" ? (CustomBackupPathBox.Text ?? "") : backupLocTag;

        DateTime? expireTime = ExpireTimePicker.SelectedDate;

        var req = new UpdateServerRequest
        {
            ID = _instanceId,
            Name = NameBox.Text ?? "",
            Base = BaseBox.Text ?? "",
            Java = javaVal,
            Core = CoreBox.Text ?? "",
            MinM = minMValue,
            MaxM = maxMValue,
            Args = argsVal,
            ForceExitDelay = (int)(ForceExitDelayBox.Value ?? 10),
            StopCommand = StopCommandBox.Text ?? "stop",
            YggdrasilApiAddr = yggdrasil,
            BackupMaxCount = (int)(BackupMaxCountBox.Value ?? 20),
            BackupDelay = (int)(BackupDelayBox.Value ?? 10),
            BackupPath = backupPathVal,
            AllowOriginASCIIColors = AllowOriginASCIIColorsToggle.IsChecked ?? true,
            MonitorPlayers = MonitorPlayersToggle.IsChecked ?? true,
            AutoRestart = AutoRestartToggle.IsChecked ?? false,
            ForceAutoRestart = ForceAutoRestartToggle.IsChecked ?? true,
            RunOnStartup = RunOnStartupToggle.IsChecked ?? false,
            IgnoreEula = IgnoreEulaToggle.IsChecked ?? false,
            ForceJvmUTF8 = ForceJvmUTF8Toggle.IsChecked ?? false,
            InputEncoding = GetSelectedComboTag(InputEncodingCombo) ?? "utf-8",
            OutputEncoding = GetSelectedComboTag(OutputEncodingCombo) ?? "utf-8",
            FileEncoding = GetSelectedComboTag(FileEncodingCombo) ?? "utf-8",
            ServerPropertiesPath = NormalizeRelativeInstancePath(ServerPropertiesPathBox.Text, "server.properties"),
            PluginsPath = NormalizeRelativeInstancePath(PluginsPathBox.Text, "plugins"),
            ModsPath = NormalizeRelativeInstancePath(ModsPathBox.Text, "mods"),
            WorldPath = NormalizeRelativeInstancePath(WorldPathBox.Text, "world"),
            RegionPath = NormalizeRelativeInstancePath(RegionPathBox.Text, "region"),
            BindFrpId = bindFrpIdVal,
            DockerImage = dockerImg,
            DockerWorkingDir = workingDir,
            DockerVolumes = string.IsNullOrEmpty(dockerVolumesVal) ? null : dockerVolumesVal,
            DockerEnvVars = string.IsNullOrEmpty(dockerEnvVarsVal) ? null : dockerEnvVarsVal,
            DockerNetworkMode = DockerNetworkModeBox.Text ?? "bridge",
            DockerNetworkAlias = string.IsNullOrEmpty(DockerNetworkAliasBox.Text) ? null : DockerNetworkAliasBox.Text,
            DockerPorts = dockerPortsVal,
            DockerCpuPercentage = (int?)DockerCpuPercentageBox.Value,
            DockerCpuCores = string.IsNullOrEmpty(DockerCpuCoresBox.Text) ? null : DockerCpuCoresBox.Text,
            DockerMaxMemoryMb = (int?)DockerMaxMemoryMbBox.Value,
            DockerMaxSwapMb = (int?)DockerMaxSwapMbBox.Value,
            DockerMaxStorage = string.IsNullOrEmpty(DockerMaxStorageBox.Text) ? null : DockerMaxStorageBox.Text,
            DockerUploadRate = string.IsNullOrEmpty(DockerUploadRateBox.Text) ? null : DockerUploadRateBox.Text,
            DockerDownloadRate = string.IsNullOrEmpty(DockerDownloadRateBox.Text) ? null : DockerDownloadRateBox.Text,
            DockerExtraArgs = string.IsNullOrEmpty(DockerExtraArgsBox.Text) ? null : DockerExtraArgsBox.Text,
            DockerExtraHosts = string.IsNullOrEmpty(dockerExtraHostsVal) ? null : dockerExtraHostsVal,
            ExpireTime = expireTime,
            CoreUrl = _pendingCoreUrl,
            CoreSha256 = _pendingCoreSha256,
            CoreFileKey = _pendingCoreFileKey
        };

        // 处理 Custom / MCDR 模式 Magic Numbers
        if (IsCustomLikeMode(javaTypeTag))
        {
            req.Core = "none";
            req.MinM = 1027;
            req.MaxM = 1102;
            req.Java = "none";
            req.CoreUrl = null;
            req.CoreSha256 = null;
            req.CoreFileKey = null;
        }

        return req;
    }

    #endregion

    #region 视图可见性与模式切换逻辑

    private void OnJavaTypeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        string tag = GetSelectedComboTag(JavaTypeCombo) ?? "online";

        if (IsMcdrMode(tag))
        {
            ApplyMcdrDefaults();
        }

        UpdateModeVisibilities();
    }

    private void UpdateModeVisibilities()
    {
        string tag = GetSelectedComboTag(JavaTypeCombo) ?? "online";

        OnlineJavaCombo.IsVisible = tag == "online";
        LocalJavaCombo.IsVisible = tag == "local";
        CustomJavaPathBox.IsVisible = tag == "custom";
        DockerJavaVersionCombo.IsVisible = tag == "docker-java" || tag == "mcdr-docker-java";

        bool isMcdr = IsMcdrMode(tag);
        bool showJavaOnly = IsShowJavaOnlyMode(tag);
        bool isDockerMode = IsDockerMode(tag);

        McdrPanel.IsVisible = isMcdr;
        ArgsTitleText.Text = (tag == "none" || tag == "docker-custom") ? "启动命令 (Command)" : "启动参数 (JVM Args)";
        ArgsDescText.Text = (tag == "none" || tag == "docker-custom")
            ? "完全自定义的启动命令。程序将直接执行此段内容，不依赖 Java 环境。"
            : "传递给 Java 的启动参数，如 GC 策略 (例如 -XX:+UseG1GC)";

        DockerGroup.IsVisible = isDockerMode;
        DockerImageBox.IsEnabled = tag != "docker-java" && tag != "mcdr-docker-java";

        CoreGroup.IsVisible = showJavaOnly;
        ResourceGroup.IsVisible = showJavaOnly;
        AuthGroup.IsVisible = showJavaOnly;
        ForceJvmUtf8Panel.IsVisible = showJavaOnly;
    }

    private static bool IsMcdrMode(string tag)
        => tag is "mcdr" or "mcdr-docker-java" or "mcdr-docker-custom";

    private static bool IsCustomLikeMode(string tag)
        => tag is "none" or "mcdr" or "mcdr-docker-java" or "mcdr-docker-custom";

    private static bool IsShowJavaOnlyMode(string tag)
        => tag is "online" or "local" or "custom" or "env" or "docker-java";

    private static bool IsDockerMode(string tag)
        => tag is "docker-java" or "docker-custom" or "mcdr-docker-java" or "mcdr-docker-custom";

    private void ApplyMcdrDefaults()
    {
        StopCommandBox.Text = "stop";
        MonitorPlayersToggle.IsChecked = true;
        SelectComboByTag(InputEncodingCombo, "utf-8");
        SelectComboByTag(OutputEncodingCombo, "utf-8");

        if (ServerPropertiesPathBox.Text == "server.properties" || string.IsNullOrEmpty(ServerPropertiesPathBox.Text))
            ServerPropertiesPathBox.Text = "server/server.properties";
        if (PluginsPathBox.Text == "plugins" || string.IsNullOrEmpty(PluginsPathBox.Text))
            PluginsPathBox.Text = "server/plugins";
        if (ModsPathBox.Text == "mods" || string.IsNullOrEmpty(ModsPathBox.Text))
            ModsPathBox.Text = "server/mods";
        if (WorldPathBox.Text == "world" || string.IsNullOrEmpty(WorldPathBox.Text))
            WorldPathBox.Text = "server/world";
    }

    private void OnMcdrPythonTextChanged(object? sender, TextChangedEventArgs e)
    {
        string py = (McdrPythonBox.Text ?? "python").Trim();
        string quoted = py.Contains(' ') && !py.StartsWith("\"") ? $"\"{py}\"" : py;
        McdrLaunchPreviewText.Text = $"实际启动命令: {quoted} -m mcdreforged start";
    }

    #endregion

    #region 事件与交互

    private void OnTogglePathEditClick(object? sender, RoutedEventArgs e)
    {
        IsPathEditable = !IsPathEditable;
        if (IsPathEditable)
        {
            ShowToast("风险操作", "修改实例路径会导致面板无法找到原有文件。请确保您已手动移动了文件，或您明确知道自己在做什么。", NotificationType.Warning);
        }
    }

    public bool IsPathEditable
    {
        get => _isPathEditable;
        set
        {
            _isPathEditable = value;
            BaseBox.IsReadOnly = !_isPathEditable;
            BasePathNoticeText.Text = _isPathEditable ? "警告：修改路径可能导致无法找到原文件" : "服务器文件的物理存储路径，非必要请勿修改";
            PathLockIcon.Kind = _isPathEditable ? Material.Icons.MaterialIconKind.LockOpenVariant : Material.Icons.MaterialIconKind.Lock;
        }
    }

    private void OnMemoryUnitSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        // 界面逻辑在 BuildUpdateSettings 时动态换算 MB
    }

    private void OnBackupLocationSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        string tag = GetSelectedComboTag(BackupLocationCombo) ?? "MSLX://Backup/Instance";
        CustomBackupPathBox.IsVisible = tag == "custom";
    }

    private void OnAuthSelectSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        string tag = GetSelectedComboTag(AuthSelectCombo) ?? "none";
        CustomAuthUrlBox.IsVisible = tag == "custom";
    }

    private void OnAutoRestartToggleCheckedChanged(object? sender, RoutedEventArgs e)
    {
        ForceAutoRestartPanel.IsVisible = AutoRestartToggle.IsChecked ?? false;
    }

    private void OnPortModeRadioChecked(object? sender, RoutedEventArgs e)
    {
        PortMappingContainer.IsVisible = PortModeMappedRadio.IsChecked == true;
        if (PortModeHostRadio.IsChecked == true)
        {
            DockerNetworkModeBox.Text = "host";
            DockerNetworkAliasBox.Text = "";
        }
        else if (DockerNetworkModeBox.Text == "host")
        {
            DockerNetworkModeBox.Text = "bridge";
        }
    }

    private void OnWorkDirRadioChecked(object? sender, RoutedEventArgs e)
    {
        DockerWorkingDirBox.IsVisible = WorkDirCustomRadio.IsChecked == true;
    }

    private void OnFrpModeRadioChecked(object? sender, RoutedEventArgs e)
    {
        FrpMultiSelectScroll.IsVisible = FrpSelectModeRadio.IsChecked == true;
        BindFrpIdBox.IsVisible = FrpManualModeRadio.IsChecked == true;
    }

    #endregion

    #region 列表动态增删行 (Port, Volume, Env, Host)

    private void OnAddPortRowClick(object? sender, RoutedEventArgs e)
        => PortList.Add(new PortMappingItem { Host = "", Container = "", Protocol = "tcp" });

    private void OnRemovePortRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is PortMappingItem item)
            PortList.Remove(item);
    }

    private void OnAddVolumeRowClick(object? sender, RoutedEventArgs e)
        => VolumeList.Add(new VolumeMappingItem { Host = "", Container = "" });

    private void OnRemoveVolumeRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is VolumeMappingItem item)
            VolumeList.Remove(item);
    }

    private void OnAddEnvRowClick(object? sender, RoutedEventArgs e)
        => EnvList.Add(new EnvVarItem { Key = "", Value = "" });

    private void OnRemoveEnvRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is EnvVarItem item)
            EnvList.Remove(item);
    }

    private void OnAddHostRowClick(object? sender, RoutedEventArgs e)
        => HostList.Add(new HostMappingItem { Domain = "", IP = "" });

    private void OnRemoveHostRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is HostMappingItem item)
            HostList.Remove(item);
    }

    #endregion

    #region 核心管理 (版本库内嵌与分片上传)

    private void OnToggleCoreToolsClick(object? sender, RoutedEventArgs e)
    {
        CoreToolsPanel.IsVisible = !CoreToolsPanel.IsVisible;
        ToggleCoreToolsBtn.Content = CoreToolsPanel.IsVisible ? "收起工具" : "文件工具";
    }

    private void OnOpenCoreSelectorClick(object? sender, RoutedEventArgs e)
    {
        // 切换内嵌 ServerCoreSelectorView 可见性，无弹窗体验
        InlineCoreSelector.IsVisible = !InlineCoreSelector.IsVisible;
    }

    private async void OnUploadCoreFileClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Jar 核心文件",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Jar File") { Patterns = new[] { "*.jar" } } }
        });

        if (files == null || files.Count == 0) return;

        var file = files[0];
        string fileName = file.Name;

        // 若之前有上传Key则删除旧缓存
        if (!string.IsNullOrEmpty(_pendingCoreFileKey))
        {
            await DaemonAPIService.DeleteUploadAsync(_pendingCoreFileKey);
        }

        UploadProgressPanel.IsVisible = true;
        UploadProgressBar.Value = 0;
        UploadStatusText.Text = $"正在上传: {fileName} (0%)";

        try
        {
            var initRes = await DaemonAPIService.InitFileUploadAsync();
            if (!initRes.Success || string.IsNullOrEmpty(initRes.UploadId))
            {
                ShowToast("上传失败", initRes.Message ?? "初始化上传失败", NotificationType.Error);
                UploadProgressPanel.IsVisible = false;
                return;
            }

            string uploadId = initRes.UploadId;
            using var stream = await file.OpenReadAsync();
            long totalSize = stream.Length;
            int chunkSize = 5 * 1024 * 1024;
            int totalChunks = (int)Math.Ceiling((double)totalSize / chunkSize);

            byte[] buffer = new byte[chunkSize];
            for (int i = 0; i < totalChunks; i++)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, chunkSize);
                byte[] chunkData = buffer;
                if (bytesRead < chunkSize)
                {
                    chunkData = new byte[bytesRead];
                    Array.Copy(buffer, chunkData, bytesRead);
                }

                var chunkRes = await DaemonAPIService.UploadFileChunkAsync(uploadId, i, chunkData);
                if (!chunkRes.Success)
                {
                    ShowToast("分片上传失败", chunkRes.Message ?? $"分片 {i} 上传失败", NotificationType.Error);
                    UploadProgressPanel.IsVisible = false;
                    return;
                }

                int prog = (int)((double)(i + 1) / totalChunks * 100);
                UploadProgressBar.Value = prog;
                UploadStatusText.Text = $"正在上传: {fileName} ({prog}%)";
            }

            var finishRes = await DaemonAPIService.FinishFileUploadAsync(uploadId, totalChunks);
            if (!finishRes.Success)
            {
                ShowToast("完成上传失败", finishRes.Message ?? "合并分片失败", NotificationType.Error);
                UploadProgressPanel.IsVisible = false;
                return;
            }

            _pendingCoreFileKey = uploadId;
            _pendingCoreUrl = null;
            CoreBox.Text = fileName;

            ShowToast("文件就绪", "文件上传就绪，保存设置后生效。", NotificationType.Success);
        }
        catch (Exception ex)
        {
            ShowToast("上传异常", ex.Message, NotificationType.Error);
        }
        finally
        {
            UploadProgressPanel.IsVisible = false;
        }
    }

    #endregion

    #region 保存与 SignalR 监听

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        if (_original != null)
        {
            FillForm(_original);
            ShowToast("已重置", "表单已重置为初始配置", NotificationType.Information);
        }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        SaveBtn.IsEnabled = false;
        try
        {
            var req = BuildUpdateSettings();

            // 规则验证
            string javaTag = GetSelectedComboTag(JavaTypeCombo) ?? "online";
            if (!IsCustomLikeMode(javaTag))
            {
                if (string.IsNullOrWhiteSpace(req.Name))
                {
                    ShowToast("验证错误", "服务器名称不能为空", NotificationType.Error);
                    return;
                }
                if (string.IsNullOrWhiteSpace(req.Base))
                {
                    ShowToast("验证错误", "基础路径不能为空", NotificationType.Error);
                    return;
                }
                if (string.IsNullOrWhiteSpace(req.Java))
                {
                    ShowToast("验证错误", "Java 环境不能为空", NotificationType.Error);
                    return;
                }
                if (string.IsNullOrWhiteSpace(req.Core))
                {
                    ShowToast("验证错误", "核心文件名不能为空", NotificationType.Error);
                    return;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(req.Args))
                {
                    ShowToast("验证错误", "自定义模式必须填写启动命令", NotificationType.Error);
                    return;
                }
            }

            // FRP 格式验证
            if (!string.IsNullOrEmpty(req.BindFrpId))
            {
                var ids = req.BindFrpId.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (!ids.All(id => Regex.IsMatch(id.Trim(), @"^[0-9]{8}$")))
                {
                    ShowToast("验证错误", "绑定的 FRP ID 必须是 8 位数字，多个请用英文逗号隔开", NotificationType.Error);
                    return;
                }
            }

            // 提交 API 请求
            var response = await DaemonAPIService.PostApiAsync(
                $"/api/instance/settings/general/{_instanceId}",
                null,
                HttpService.PostContentType.Json,
                req);

            if (!response.IsSuccess)
            {
                ShowToast("保存失败", response.Exception?.Message ?? "请求失败", NotificationType.Error);
                OnSaveResult?.Invoke($"保存失败: {response.Exception?.Message}", false);
                return;
            }

            JObject json = JObject.Parse(response.Content);
            bool isOk = json["code"]?.ToString() == "200";
            if (!isOk)
            {
                string msg = json["message"]?.ToString() ?? "保存失败";
                ShowToast("保存失败", msg, NotificationType.Error);
                OnSaveResult?.Invoke($"保存失败: {msg}", false);
                return;
            }

            bool needListen = json["data"]?["needListen"]?.Value<bool>() ?? false;
            if (needListen)
            {
                await StartSignalRListeningAsync();
            }
            else
            {
                ShowToast("保存成功", "配置已成功更新！", NotificationType.Success);
                OnSaveResult?.Invoke("保存成功", true);
                await LoadAsync(_instanceId);
            }
        }
        catch (Exception ex)
        {
            ShowToast("保存失败", ex.Message, NotificationType.Error);
            OnSaveResult?.Invoke($"保存失败: {ex.Message}", false);
        }
        finally
        {
            SaveBtn.IsEnabled = true;
        }
    }

    private async Task StartSignalRListeningAsync()
    {
        ProgressOverlay.IsVisible = true;
        SignalRProgressBar.Value = 0;
        SignalRProgressText.Text = "0%";
        ProgressLogsTextBlock.Text = "";
        CloseProgressBtn.IsVisible = false;

        _updateSignalR = new UpdateProgressSignalRService();
        _updateSignalR.UpdateStatusReceived += (msg, prog, isErr) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                string timeStr = DateTime.Now.ToString("HH:mm:ss");
                string logLine = isErr ? $"[{timeStr}] [错误] {msg}\n" : $"[{timeStr}] {msg}\n";
                ProgressLogsTextBlock.Text += logLine;
                LogScrollViewer.ScrollToEnd();

                if (prog >= 0)
                {
                    SignalRProgressBar.Value = prog;
                    SignalRProgressText.Text = $"{prog:F1}%";
                }

                if (prog == 100 || isErr || prog == -1)
                {
                    CloseProgressBtn.IsVisible = true;
                    if (prog == 100)
                    {
                        ShowToast("更新完成", "服务器核心与配置更新成功", NotificationType.Success);
                    }
                }
            });
        };

        try
        {
            await _updateSignalR.ConnectAsync(_instanceId);
        }
        catch (Exception ex)
        {
            ProgressLogsTextBlock.Text += $"[-] 连接 SignalR 失败: {ex.Message}\n";
            CloseProgressBtn.IsVisible = true;
        }
    }

    private async void OnCloseProgressClick(object? sender, RoutedEventArgs e)
    {
        if (_updateSignalR != null)
        {
            await _updateSignalR.DisposeAsync();
            _updateSignalR = null;
        }

        ProgressOverlay.IsVisible = false;
        await LoadAsync(_instanceId);
    }

    #endregion

    #region 通用路径与 Toast 辅助

    private static void ShowToast(string title, string message, NotificationType type = NotificationType.Information)
    {
        DialogService.ToastManager.CreateToast()
            .OfType(type)
            .WithTitle(title)
            .WithContent(message)
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .Queue();
    }

    private static string NormalizeRelativeInstancePath(string? value, string defaultPath)
    {
        string normalized = (value ?? defaultPath).Trim().Replace('\\', '/');
        if (string.IsNullOrEmpty(normalized)) return defaultPath;
        normalized = Regex.Replace(normalized, @"/+", "/");
        if (normalized.StartsWith("./")) normalized = normalized.Substring(2);
        return normalized;
    }

    private static void SelectComboByTag(ComboBox box, string tagValue)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            string itemTag = item.Tag?.ToString() ?? item.Content?.ToString() ?? "";
            if (string.Equals(itemTag, tagValue, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    private static string? GetSelectedComboTag(ComboBox box)
    {
        if (box.SelectedItem is ComboBoxItem item)
        {
            return item.Tag?.ToString() ?? item.Content?.ToString();
        }
        return null;
    }

    #endregion
}

#region 动态列表 Item Models

public class PortMappingItem : INotifyPropertyChanged
{
    private string _host = "";
    private string _container = "";
    private string _protocol = "tcp";

    public string Host { get => _host; set => SetField(ref _host, value); }
    public string Container { get => _container; set => SetField(ref _container, value); }
    public string Protocol { get => _protocol; set => SetField(ref _protocol, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}

public class VolumeMappingItem : INotifyPropertyChanged
{
    private string _host = "";
    private string _container = "";

    public string Host { get => _host; set => SetField(ref _host, value); }
    public string Container { get => _container; set => SetField(ref _container, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}

public class EnvVarItem : INotifyPropertyChanged
{
    private string _key = "";
    private string _value = "";

    public string Key { get => _key; set => SetField(ref _key, value); }
    public string Value { get => _value; set => SetField(ref _value, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}

public class HostMappingItem : INotifyPropertyChanged
{
    private string _domain = "";
    private string _ip = "";

    public string Domain { get => _domain; set => SetField(ref _domain, value); }
    public string IP { get => _ip; set => SetField(ref _ip, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}

public class FrpTunnelItem : INotifyPropertyChanged
{
    private string _id = "";
    private string _label = "";
    private bool _isSelected;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string Label { get => _label; set => SetField(ref _label, value); }
    public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}

#endregion

