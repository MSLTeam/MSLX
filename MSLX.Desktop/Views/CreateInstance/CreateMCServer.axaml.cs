using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using SukiUI.Toasts;
using System;
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

    public CreateMCServer()
    {
        InitializeComponent();
        UpdateStepVisibility();
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
                BtnNext.Content = "创建服务器";
                UpdateConfirmationInfo();
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
        // 验证当前步骤
        if (!await ValidateCurrentStep())
            return;

        if (_currentStep < 3)
        {
            _currentStep++;
            UpdateStepVisibility();
        }
        else
        {
            // 最后一步，创建服务器
            await CreateServerAsync();
        }
    }

    /// <summary>
    /// 验证当前步骤
    /// </summary>
    private async Task<bool> ValidateCurrentStep()
    {
        switch (_currentStep)
        {
            case 0: // 基础配置
                if (string.IsNullOrWhiteSpace(TxtServerName.Text))
                {
                    await ShowErrorMessage("验证失败", "请输入服务器名称");
                    TxtServerName.Focus();
                    return false;
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
                        await ShowErrorMessage("验证失败", "请输入核心下载URL");
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
                if(string.IsNullOrEmpty(TxtCoreName.Text) || string.IsNullOrWhiteSpace(TxtCoreName.Text))
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
        TxtConfirmMemory.Text = $"{(int)(NumMinMemory.Value ?? 0)} MB - {(int)(NumMaxMemory.Value ?? 0)} MB";

        // 路径
        TxtConfirmPath.Text = string.IsNullOrWhiteSpace(TxtStoragePath.Text)
            ? $"默认路径：{Path.Combine(ConfigService.GetAppDataPath(), "Server")}"
            : TxtStoragePath.Text;

        TxtConfirmJava.Text = string.IsNullOrWhiteSpace(TxtJavaPath.Text)
            ? "环境变量：java"
            : TxtJavaPath.Text;
    }

    #endregion

    #region 核心来源选择

    /// <summary>
    /// 核心来源改变
    /// </summary>
    private void CoreSourceChanged(object? sender, RoutedEventArgs e)
    {
        PanelLocalUpload.IsVisible = RbLocalUpload?.IsChecked == true;
        TxtCoreName.IsEnabled = RbLocalUpload?.IsChecked == false;
        PanelDownloadCore.IsVisible = RbDownloadCore?.IsChecked == true;
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
            FileTypeFilter = new[]
            {
                new FilePickerFileType("服务端文件")
                {
                    Patterns = new[] { "*.jar" }
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = new[] { "*" }
                }
            }
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
            if ((!initResult.Success)||string.IsNullOrEmpty(initResult.UploadId))
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
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Java可执行文件")
                {
                    Patterns = new[] { "java", "java.exe" }
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = new[] { "*" }
                }
            }
        });

        if (files.Count > 0)
        {
            TxtJavaPath.Text = files[0].Path.LocalPath;
        }
    }

    #endregion

    #region 创建服务器

    /// <summary>
    /// 创建服务器
    /// </summary>
    private async Task CreateServerAsync()
    {
        StatusBorder.IsVisible = true;
        TxtStatus.Text = "正在创建服务器实例...";
        ProgressBar.IsIndeterminate = true;
        BtnNext.IsEnabled = false;
        BtnPrevious.IsEnabled = false;

        try
        {
            // 构建请求
            var request = BuildCreateRequest();

            // 调用API
            var result = await DaemonAPIService.CreateServerInstanceAsync(request);

            if (result.Success)
            {
                await ShowSuccessMessage("创建成功", result.Message ?? "服务器实例创建成功");
                ResetForm();
            }
            else
            {
                await ShowErrorMessage("创建失败", result.Message ?? "未知错误");
            }
        }
        catch (Exception ex)
        {
            await ShowErrorMessage("创建失败", $"发生异常: {ex.Message}");
        }
        finally
        {
            StatusBorder.IsVisible = false;
            BtnNext.IsEnabled = true;
            BtnPrevious.IsEnabled = true;

            SideMenuHelper.MainSideMenuHelper?.NavigateRemove(this);
        }
    }

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

        return new
        {
            name = TxtServerName.Text?.Trim(),
            path = string.IsNullOrWhiteSpace(TxtStoragePath.Text)
                ? Path.Combine(ConfigService.GetAppDataPath(), "Server")
                : TxtStoragePath.Text.Trim(),
            java = string.IsNullOrWhiteSpace(TxtJavaPath.Text)
                ? "java"
                : TxtJavaPath.Text.Trim(),
            core = TxtCoreName.Text?.Trim(),
            minM = (int)(NumMinMemory.Value ?? 0),
            maxM = (int)(NumMinMemory.Value ?? 0),
            args = string.IsNullOrWhiteSpace(TxtExtraArgs.Text)
                ? null
                : TxtExtraArgs.Text.Trim(),
            coreFileKey,
            coreUrl,
            coreSha256
        };
    }

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
        SideMenuHelper.MainSideMenuHelper?.NavigateRemove(this);
    }

    /// <summary>
    /// 重置表单
    /// </summary>
    private void ResetForm()
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