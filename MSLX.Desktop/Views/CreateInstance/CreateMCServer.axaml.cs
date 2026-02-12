using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.AspNetCore.SignalR.Client;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using Newtonsoft.Json.Linq;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views.CreateInstance;

public partial class CreateMCServer : UserControl
{
    private int _currentStep = 0;
    private string? _selectedFilePath;
    private string? _uploadId;
    private long _fileSize;
    private const int ChunkSize = 10240 * 1024; // 10MB per chunk

    private HubConnection? _hubConnection; // 创建进度的日志Hub连接
    private string? _createdServerId;
    private ObservableCollection<CreationLog> _logCollection = new();


    public class CreationLog // 创建实例进度日志格式
    {
        public string Time { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    // Java 选择相关数据
    private ObservableCollection<string> _onlineJavaList = new();
    private ObservableCollection<LocalJavaListModel> _localJavaList = new();

    public CreateMCServer()
    {
        InitializeComponent();
        ListLogs.ItemsSource = _logCollection;
        ComboOnlineJava.ItemsSource = _onlineJavaList;
        ComboLocalJava.ItemsSource = _localJavaList;
        UpdateStepVisibility();
        this.Loaded += (s, e) =>
        {
            RbDownloadCore.IsChecked = true;
            if (!_isOnlineJavaLoaded && TabJavaSelection.SelectedIndex == 0)
            {
                _ = LoadOnlineJavaList();
                // 非本机环境隐藏浏览本地选择Java的功能
                if (!PlatformHelper.IsLocalService())
                {
                    BtnBrowseJava.IsVisible = false;
                }
            }
        };
    }

    #region 步骤控制

    /// <summary>
    /// 更新步骤显示
    /// </summary>
    private void UpdateStepVisibility()
    {
        // 隐藏所有步骤
        Step1Panel.IsVisible = false;
        Step2Panel.IsVisible = false;
        Step3Panel.IsVisible = false;
        Step4Panel.IsVisible = false;
        Step5Panel.IsVisible = false;

        BottomActionBorder.IsVisible = true;

        // 显示当前步骤
        switch (_currentStep)
        {
            case 0:
                Step1Panel.IsVisible = true;
                BtnPrevious.IsVisible = false;
                BtnNext.Content = "下一步";
                break;
            case 1:
                Step2Panel.IsVisible = true;
                BtnPrevious.IsVisible = true;
                BtnNext.Content = "下一步";
                break;
            case 2:
                Step3Panel.IsVisible = true;
                BtnPrevious.IsVisible = true;
                BtnNext.Content = "下一步";
                break;
            case 3:
                Step4Panel.IsVisible = true;
                BtnPrevious.IsVisible = true;
                BtnNext.Content = "立即创建";
                UpdateConfirmationInfo();
                break;

            case 4:
                Step5Panel.IsVisible = true;
                BottomActionBorder.IsVisible = false; // 锁定操作，隐藏底部按钮
                break;
        }
    }

    /// <summary>
    /// 上一步
    /// </summary>
    private void BtnPrevious_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentStep > 0)
        {
            _currentStep--;
            UpdateStepVisibility();
        }
    }

    /// <summary>
    /// 下一步/创建
    /// </summary>
    private async void BtnNext_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ValidateCurrentStep())
            return;

        if (_currentStep < 3)
        {
            _currentStep++;
            UpdateStepVisibility();
        }
        else
        {
            // 进入提交流程
            await SubmitAndStartProgressAsync();
        }
    }

    /// <summary>
    /// 验证当前步骤
    /// </summary>
    private async Task<bool> ValidateCurrentStep()
    {
        switch (_currentStep)
        {
            case 0: // 基础配置 & Java
                if (string.IsNullOrWhiteSpace(TxtServerName.Text))
                {
                    await ShowErrorMessage("验证失败", "请输入服务器名称");
                    TxtServerName.Focus();
                    return false;
                }

                // 验证 Java 选择
                switch (TabJavaSelection.SelectedIndex)
                {
                    case 0: // 在线安装
                        if (ComboOnlineJava.SelectedItem == null)
                        {
                            await ShowErrorMessage("验证失败", "请选择要下载的 Java 版本");
                            return false;
                        }
                        break;
                    case 1: // 本地版本
                        if (ComboLocalJava.SelectedItem == null)
                        {
                            await ShowErrorMessage("验证失败", "请选择一个本地 Java 环境");
                            return false;
                        }
                        break;
                    case 3: // 自定义路径
                        if (string.IsNullOrWhiteSpace(TxtJavaPath.Text))
                        {
                            await ShowErrorMessage("验证失败", "请输入自定义 Java 路径");
                            return false;
                        }
                        break;
                }
                break;

            case 1: // 核心文件
                if (RbLocalUpload?.IsChecked == true)
                {
                    if (string.IsNullOrEmpty(_uploadId))
                    {
                        await ShowErrorMessage("验证失败", "请先上传核心文件");
                        return false;
                    }
                }
                else if (RbDownloadCore?.IsChecked == true)
                {
                    if (string.IsNullOrWhiteSpace(TxtCoreUrl.Text))
                    {
                        await ShowErrorMessage("验证失败", "请选择一个核心");
                        TxtCoreUrl.Focus();
                        return false;
                    }
                    if (!Uri.TryCreate(TxtCoreUrl.Text, UriKind.Absolute, out _))
                    {
                        await ShowErrorMessage("验证失败", "核心下载URL格式不正确");
                        TxtCoreUrl.Focus();
                        return false;
                    }
                }
                if (string.IsNullOrEmpty(TxtCoreName.Text) || string.IsNullOrWhiteSpace(TxtCoreName.Text))
                {
                    await ShowErrorMessage("验证失败", "请输入服务端核心文件名");
                    TxtCoreName.Focus();
                    return false;
                }
                break;

            case 2: // 内存配置
                if (NumMinMemory.Value >= NumMaxMemory.Value)
                {
                    await ShowErrorMessage("验证失败", "最大内存必须大于最小内存");
                    NumMaxMemory.Focus();
                    return false;
                }
                break;
        }

        return true;
    }

    /// <summary>
    /// 更新确认信息
    /// </summary>
    private void UpdateConfirmationInfo()
    {
        TxtConfirmServerName.Text = TxtServerName.Text;
        TxtConfirmCoreName.Text = TxtCoreName.Text;

        // 核心来源
        if (RbLocalUpload?.IsChecked == true)
            TxtConfirmCoreSource.Text = $"本地上传 (ID: {_uploadId})";
        else if (RbDownloadCore?.IsChecked == true)
            TxtConfirmCoreSource.Text = $"从URL下载: {TxtCoreUrl.Text}";

        // 内存配置
        TxtConfirmMemory.Text = $"{(int)(NumMinMemory.Value ?? 0)} MB - {(int)(NumMaxMemory.Value ?? 0)} MB"; // 修正了原本 MaxMemory 读取 MinMemory 的可能的bug

        // 路径
        TxtConfirmPath.Text = string.IsNullOrWhiteSpace(TxtStoragePath.Text)
            ? $"默认路径：{Path.Combine(ConfigService.GetAppDataPath(), "Server")}"
            : TxtStoragePath.Text;

        switch (TabJavaSelection.SelectedIndex)
        {
            case 0:
                TxtConfirmJava.Text = $"在线安装: Java {ComboOnlineJava.SelectedItem}";
                break;
            case 1:
                var local = ComboLocalJava.SelectedItem as LocalJavaListModel;
                TxtConfirmJava.Text = $"本地环境: {local?.Version} ({local?.Vendor})";
                break;
            case 2:
                TxtConfirmJava.Text = "系统环境变量 (java)";
                break;
            case 3:
                TxtConfirmJava.Text = $"自定义路径: {TxtJavaPath.Text}";
                break;
        }
    }

    #endregion

    #region Java 选择逻辑

    private bool _isOnlineJavaLoaded = false;
    private bool _isLocalJavaLoaded = false;

    /// <summary>
    /// Java 选项卡切换事件
    /// </summary>
    private async void TabJavaSelection_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not TabControl tabControl) return;

        int index = tabControl.SelectedIndex;

        if (index == 0 && !_isOnlineJavaLoaded)
        {
            await LoadOnlineJavaList();
        }
        else if (index == 1 && !_isLocalJavaLoaded)
        {
            await LoadLocalJavaList(false);
        }
    }

    /// <summary>
    /// 加载在线 Java 版本
    /// </summary>
    private async Task LoadOnlineJavaList()
    {
        if (ComboOnlineJava == null) return;
        try
        {
            ComboOnlineJava.PlaceholderText = "正在获取云端列表...";

            // 获取系统信息
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
                PlatformHelper.TheArchitecture.X64 => "x64",
                _ => "x64"
            };

            var result = await MSLAPIService.GetJsonDataAsync("/query/jdk", "data", new Dictionary<string, string>
            {
                { "os", os },
                { "arch", arch }
            });

            if (result.Success && result.Data is JArray jArray)
            {
                _onlineJavaList.Clear();
                foreach (var item in jArray)
                {
                    _onlineJavaList.Add(item.ToString());
                }

                if (_onlineJavaList.Count > 0) ComboOnlineJava.SelectedIndex = 0;
                ComboOnlineJava.PlaceholderText = "请选择版本";
                _isOnlineJavaLoaded = true;
            }
            else
            {
                ComboOnlineJava.PlaceholderText = "获取失败: " + result.Msg;
            }
        }
        catch (Exception ex)
        {
            ComboOnlineJava.PlaceholderText = "网络错误";
            await ShowErrorMessage("获取Java列表失败", ex.Message);
        }
    }

    /// <summary>
    /// 加载本地 Java 列表
    /// </summary>
    private async Task LoadLocalJavaList(bool forceRefresh)
    {
        if (ComboOnlineJava == null) return;
        try
        {
            ComboLocalJava.PlaceholderText = "正在扫描本地环境...";
            string path = "/api/java/list";
            var dict = new Dictionary<string, string>();
            if (forceRefresh) dict.Add("refresh", "true");

            var result = await DaemonAPIService.GetJsonDataAsync(path, "data", dict);

            if (result.Success && result.Data is JArray jArray)
            {
                _localJavaList.Clear();
                var list = jArray.ToObject<List<LocalJavaListModel>>();
                if (list != null)
                {
                    foreach (var item in list) _localJavaList.Add(item);
                }

                if (_localJavaList.Count > 0) ComboLocalJava.SelectedIndex = 0;
                ComboLocalJava.PlaceholderText = "请选择...";
                _isLocalJavaLoaded = true;
                if (forceRefresh)
                {
                    DialogService.ToastManager.CreateToast()
                        .OfType(Avalonia.Controls.Notifications.NotificationType.Success)
                        .WithTitle("刷新成功！")
                        .WithContent($"本地Java列表已成功刷新！")
                        .Dismiss().After(TimeSpan.FromSeconds(5))
                        .Queue();
                }
            }
            else
            {
                ComboLocalJava.PlaceholderText = "扫描失败: " + result.Msg;
            }
        }
        catch (Exception ex)
        {
            ComboLocalJava.PlaceholderText = "扫描出错";
            await ShowErrorMessage("本地扫描失败", ex.Message);
        }
    }

    private async void BtnRefreshLocalJava_Click(object? sender, RoutedEventArgs e)
    {
        await LoadLocalJavaList(true);
    }

    #endregion

    #region 核心来源选择

    /// <summary>
    /// 核心来源改变
    /// </summary>
    private void CoreSourceChanged(object? sender, RoutedEventArgs? e)
    {
        if (PanelLocalUpload == null || PanelDownloadCore == null) return;

        PanelLocalUpload.IsVisible = RbLocalUpload.IsChecked == true;
        PanelDownloadCore.IsVisible = RbDownloadCore.IsChecked == true;

        // 清理隐藏的老数据
        TxtCoreUrl.Text = string.Empty;
        TxtCoreSha256.Text = string.Empty;
        if (RbCustomCore.IsChecked != true)
        {
            TxtCoreName.Text = string.Empty;
        }

        if (RbDownloadCore.IsChecked == true)
        {
            TxtCoreName.IsEnabled = false; 
            TxtCoreName.Watermark = "点击'在线选择'以确定文件名";
        }
        else if (RbCustomCore.IsChecked == true)
        {
            TxtCoreName.IsEnabled = true;
            TxtCoreName.Watermark = "请输入文件名，如 server.jar";
        }
        else // 本地上传
        {
            TxtCoreName.IsEnabled = false;
            TxtCoreName.Watermark = "上传文件后将自动显示文件名";
        }
    }

    /// <summary>
    /// 打开在线核心选择器
    /// </summary>
    private async void BtnSelectCoreOnline_Click(object? sender, RoutedEventArgs e)
    {
        var selectorView = new ServerCoreSelectorView();
        var dialogBuilder = DialogService.DialogManager.CreateDialog()
            .WithTitle("选择服务端核心")
            .WithContent(selectorView);
        dialogBuilder.Completion = new TaskCompletionSource<bool>();

        // 订阅事件
        selectorView.OnCoreSelected += (url, sha256, filename, coreName) =>
        {
            // 回填数据
            TxtCoreUrl.Text = url;
            TxtCoreSha256.Text = sha256;

            if (string.IsNullOrWhiteSpace(TxtCoreName.Text) || TxtCoreName.Text.Contains(".jar"))
            {
                TxtCoreName.Text = filename;
            }

            // 提示
            DialogService.ToastManager.CreateToast()
                .OfType(Avalonia.Controls.Notifications.NotificationType.Success)
                .WithTitle("已选择核心")
                .WithContent($"已加载 {coreName} 的下载配置")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();

            // 关闭弹窗
            dialogBuilder.Completion.TrySetResult(true);
            dialogBuilder.Dialog.Dismiss();
        };

        dialogBuilder.WithActionButton("取消", _ =>
        {
            dialogBuilder.Completion.TrySetResult(false);
        }, true);

        await dialogBuilder.TryShowAsync();
    }

    #endregion

    #region 文件上传

    /// <summary>
    /// 选择文件
    /// </summary>
    private async void BtnSelectFile_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择服务端核心文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("服务端文件")
                {
                    Patterns = ["*.jar"]
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = ["*"]
                }
            ]
        });

        if (files.Count > 0)
        {
            _selectedFilePath = files[0].Path.LocalPath;
            var fileInfo = new FileInfo(_selectedFilePath);
            _fileSize = fileInfo.Length;

            TxtSelectedFile.Text = fileInfo.Name;
            TxtFileSize.Text = $"文件大小: {FormatFileSize(_fileSize)}";
            BtnUpload.IsEnabled = true;

            // 重置上传状态
            _uploadId = null;
            PanelUploadSuccess.IsVisible = false;
        }
    }

    /// <summary>
    /// 开始上传
    /// </summary>
    private async void BtnUpload_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedFilePath))
        {
            await ShowErrorMessage("错误", "请先选择文件");
            return;
        }

        try
        {
            BtnUpload.IsEnabled = false;
            BtnSelectFile.IsEnabled = false;
            PanelUploadProgress.IsVisible = true;

            // 1. 初始化上传
            TxtUploadStatus.Text = "初始化上传...";
            ProgressUpload.IsIndeterminate = true;

            var initResult = await DaemonAPIService.InitFileUploadAsync();
            if ((!initResult.Success) || string.IsNullOrEmpty(initResult.UploadId))
            {
                await ShowErrorMessage("上传失败", initResult.Message ?? "初始化上传失败");
                return;
            }

            _uploadId = initResult.UploadId;

            // 2. 分片上传
            ProgressUpload.IsIndeterminate = false;
            ProgressUpload.Value = 0;

            var totalChunks = (int)Math.Ceiling((double)_fileSize / ChunkSize);

            using (var fileStream = File.OpenRead(_selectedFilePath))
            {
                for (int i = 0; i < totalChunks; i++)
                {
                    var buffer = new byte[Math.Min(ChunkSize, _fileSize - i * ChunkSize)];
                    await fileStream.ReadExactlyAsync(buffer, 0, buffer.Length);

                    TxtUploadStatus.Text = $"上传分片 {i + 1}/{totalChunks}...";

                    var chunkResult = await DaemonAPIService.UploadFileChunkAsync(_uploadId, i, buffer);
                    if (!chunkResult.Success)
                    {
                        await ShowErrorMessage("上传失败", $"分片 {i + 1} 上传失败: {chunkResult.Message}");
                        await DaemonAPIService.DeleteUploadAsync(_uploadId); // 清理
                        return;
                    }

                    // 更新进度
                    var progress = (double)(i + 1) / totalChunks * 100;
                    ProgressUpload.Value = progress;
                    TxtUploadPercent.Text = $"{progress:F1}%";
                }
            }

            // 3. 完成上传
            TxtUploadStatus.Text = "正在合并文件...";
            ProgressUpload.IsIndeterminate = true;

            var finishResult = await DaemonAPIService.FinishFileUploadAsync(_uploadId, totalChunks);
            if (!finishResult.Success)
            {
                await ShowErrorMessage("上传失败", finishResult.Message ?? "完成上传失败");
                await DaemonAPIService.DeleteUploadAsync(_uploadId);
                return;
            }

            // 显示成功信息
            PanelUploadProgress.IsVisible = false;
            PanelUploadSuccess.IsVisible = true;
            TxtUploadId.Text = $"Upload ID: {_uploadId}";
            TxtCoreName.Text = Path.GetFileName(_selectedFilePath);
            await ShowSuccessMessage("上传成功", "核心文件已成功上传");
        }
        catch (Exception ex)
        {
            await ShowErrorMessage("上传失败", $"发生异常: {ex.Message}");
            if (!string.IsNullOrEmpty(_uploadId))
            {
                await DaemonAPIService.DeleteUploadAsync(_uploadId);
            }
        }
        finally
        {
            BtnUpload.IsEnabled = false;
            BtnSelectFile.IsEnabled = true;
        }
    }

    /// <summary>
    /// 格式化文件大小
    /// </summary>
    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    #endregion

    #region 路径选择

    /// <summary>
    /// 浏览存储路径
    /// </summary>
    private async void BtnBrowsePath_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folder = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择服务器存储路径",
            AllowMultiple = false
        });

        if (folder.Count > 0)
        {
            TxtStoragePath.Text = folder[0].Path.LocalPath;
        }
    }

    /// <summary>
    /// 浏览Java路径
    /// </summary>
    private async void BtnBrowseJava_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择Java可执行文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Java可执行文件")
                {
                    Patterns = ["java", "java.exe"]
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = ["*"]
                }
            ]
        });

        if (files.Count > 0)
        {
            TxtJavaPath.Text = files[0].Path.LocalPath;
        }
    }

    #endregion

    #region 创建服务器

    /// <summary>
    /// 构建创建请求
    /// </summary>
    private object BuildCreateRequest()
    {
        string? coreFileKey = null;
        string? coreUrl = null;
        string? coreSha256 = null;

        if (RbLocalUpload?.IsChecked == true)
        {
            coreFileKey = _uploadId;
        }
        else if (RbDownloadCore?.IsChecked == true)
        {
            coreUrl = TxtCoreUrl.Text?.Trim();
            coreSha256 = string.IsNullOrWhiteSpace(TxtCoreSha256.Text)
                ? null
                : TxtCoreSha256.Text.Trim();
        }

        string javaCmd = "java";

        switch (TabJavaSelection.SelectedIndex)
        {
            case 0: // 在线安装
                var version = ComboOnlineJava.SelectedItem?.ToString();
                if (!string.IsNullOrEmpty(version))
                    javaCmd = $"MSLX://Java/{version}";
                break;

            case 1: // 本地版本
                var selected = ComboLocalJava.SelectedItem as LocalJavaListModel;
                if (selected != null)
                    javaCmd = selected.Path;
                break;

            case 2: // 环境变量
                javaCmd = "java";
                break;

            case 3: // 自定义路径
                javaCmd = TxtJavaPath.Text?.Trim() ?? "java";
                break;
        }

        return new
        {
            name = TxtServerName.Text?.Trim(),
            path = string.IsNullOrWhiteSpace(TxtStoragePath.Text)
                ? Path.Combine(ConfigService.GetAppDataPath(), "Server")
                : TxtStoragePath.Text.Trim(),
            java = javaCmd,
            core = TxtCoreName.Text?.Trim(),
            minM = (int)(NumMinMemory.Value ?? 0),
            maxM = (int)(NumMaxMemory.Value ?? 0), 
            args = string.IsNullOrWhiteSpace(TxtExtraArgs.Text)
                ? null
                : TxtExtraArgs.Text.Trim(),
            coreFileKey,
            coreUrl,
            coreSha256
        };
    }

    #region 提交与进度
    private async Task SubmitAndStartProgressAsync()
    {
        try
        {
            // 锁定界面
            StatusBorder.IsVisible = true;
            TxtStatus.Text = "正在提交创建请求...";
            ProgressBar.IsIndeterminate = true;
            BtnNext.IsEnabled = false;
            BtnPrevious.IsEnabled = false;

            // 创建实例
            var request = BuildCreateRequest();
            var result = await DaemonAPIService.CreateServerInstanceAsync(request);
            _createdServerId = result.Data?["serverId"]?.ToString() ?? result.Data?["id"]?.ToString();
            if ((!result.Success) || _createdServerId == null)
            {
                await ShowErrorMessage("创建请求失败", result.Message ?? "未知错误");
                // 恢复界面
                StatusBorder.IsVisible = false;
                BtnNext.IsEnabled = true;
                BtnPrevious.IsEnabled = true;
                return;
            }

            _currentStep = 4; // 切换到进度页
            UpdateStepVisibility();

            // 初始化进度页状态
            PanelCreating.IsVisible = true;
            PanelSuccess.IsVisible = false;
            _logCollection.Clear();
            ProgressCreation.Value = 0;

            // 启动 SignalR 连接
            await StartSignalRConnection(_createdServerId);

        }
        catch (Exception ex)
        {
            await ShowErrorMessage("系统错误", ex.Message);
            // 恢复界面状态
            StatusBorder.IsVisible = false;
            BtnNext.IsEnabled = true;
            BtnPrevious.IsEnabled = true;
        }
    }

    // SignalR 连接与监听
    private async Task StartSignalRConnection(string serverId)
    {
        try
        {
            AddLog("正在连接到实时进度服务...");

            var baseUrl = ConfigStore.DaemonAddress;
            var apiKey = ConfigStore.DaemonApiKey;

            var hubUrl = $"{baseUrl}/api/hubs/creationProgressHub?x-api-key={apiKey}";

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            // 监听状态更新
            _hubConnection.On<string, string, double?>("StatusUpdate", async (id, message, progress) =>
            {
                if (id != serverId) return;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AddLog(message);

                    if (progress.HasValue && progress.Value >= 0)
                    {
                        ProgressCreation.Value = progress.Value;
                    }

                    if (progress == 100)
                    {
                        HandleCreationSuccess();
                    }
                    else if (progress == -1)
                    {
                        HandleCreationError(message);
                    }
                });
            });

            await _hubConnection.StartAsync();
            AddLog("连接成功，等待服务器响应...");

            // 订阅任务
            await _hubConnection.InvokeAsync("TrackServer", serverId);
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AddLog($"连接失败: {ex.Message}");
                _ = ShowErrorMessage("连接失败", "无法连接到进度服务器，请稍后在列表查看结果。");
            });

            // 停止连接
            await DisposeSignalR();
        }
    }

    // 显示日志
    private void AddLog(string message)
    {
        _logCollection.Add(new CreationLog
        {
            Time = DateTime.Now.ToString("HH:mm:ss"),
            Message = message
        });

        // 自动滚动到底部
        if (ListLogs.ItemCount > 0)
        {
            var lastItem = ListLogs.Items[ListLogs.ItemCount - 1];
            if (lastItem != null) //避免传递 null
            {
                ListLogs.ScrollIntoView(lastItem);
            }
        }
    }

    // 处理成功
    private async void HandleCreationSuccess()
    {
        // 停止连接
        await DisposeSignalR();

        TxtCreatedServerId.Text = $"Server ID: {_createdServerId}";
        PanelCreating.IsVisible = false;
        PanelSuccess.IsVisible = true;

    }

    // 处理错误
    private async void HandleCreationError(string message)
    {
        // 停止连接
        await DisposeSignalR();

        await ShowErrorMessage("创建失败", message);

        await Task.Delay(2000);
        _currentStep = 0;
        UpdateStepVisibility();
    }

    private async Task DisposeSignalR()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.StopAsync();
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }

    // 返回列表
    private void BtnReturnList_Click(object? sender, RoutedEventArgs e)
    {
        StatusBorder.IsVisible = false;
        BtnNext.IsEnabled = true;
        BtnPrevious.IsEnabled = true;
        BottomActionBorder.IsVisible = true;

        ResetForm();
        SideMenuHelper.MainSideMenuHelper?.NavigateTo<InstanceListPage>();
        SideMenuHelper.MainSideMenuHelper?.NavigateRemove(this);

        _ = InstanceListPage.LoadServersList();
    }

    private void BtnGoConsole_Click(object? sender, RoutedEventArgs e)
    {
        // 导航到控制台
        // 控制台在哪里？我不知道喔~

    }
    #endregion

    #endregion

    #region 取消&重置

    /// <summary>
    /// 取消
    /// </summary>
    private async void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        // 清理
        if (!string.IsNullOrEmpty(_uploadId))
        {
            await DaemonAPIService.DeleteUploadAsync(_uploadId);
        }

        ResetForm();
        SideMenuHelper.MainSideMenuHelper?.NavigateTo<InstanceListPage>();
        SideMenuHelper.MainSideMenuHelper?.NavigateRemove(this);
    }

    /// <summary>
    /// 重置表单
    /// </summary>
    private async void ResetForm()
    {
        // 重置步骤
        _currentStep = 0;
        UpdateStepVisibility();

        // 清空输入
        TxtServerName.Text = string.Empty;
        TxtStoragePath.Text = string.Empty;
        TxtJavaPath.Text = string.Empty;
        TxtCoreName.Text = string.Empty;
        TxtCoreUrl.Text = string.Empty;
        TxtCoreSha256.Text = string.Empty;
        TxtExtraArgs.Text = string.Empty;
        NumMinMemory.Value = 1024;
        NumMaxMemory.Value = 4096;

        // 重置上传状态
        _uploadId = null;
        _selectedFilePath = null;
        TxtSelectedFile.Text = "未选择文件";
        TxtFileSize.Text = string.Empty;
        BtnUpload.IsEnabled = false;
        PanelUploadProgress.IsVisible = false;
        PanelUploadSuccess.IsVisible = false;

        // 重置单选按钮
        RbLocalUpload.IsChecked = true;

        await DisposeSignalR();
    }

    #endregion

    /// <summary>
    /// 显示错误消息
    /// </summary>
    private async Task ShowErrorMessage(string title, string message)
    {
        DialogService.ToastManager.CreateToast()
            .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
            .WithTitle(title)
            .WithContent(message)
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .Queue();
    }

    /// <summary>
    /// 显示成功消息
    /// </summary>
    private async Task ShowSuccessMessage(string title, string? message)
    {
        DialogService.ToastManager.CreateToast()
            .OfType(Avalonia.Controls.Notifications.NotificationType.Success)
            .WithTitle(title)
            .WithContent(message)
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .Queue();
    }
}