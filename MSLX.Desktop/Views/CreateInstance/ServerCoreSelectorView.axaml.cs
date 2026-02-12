using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MSLX.Desktop.Utils.API;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views.CreateInstance;

public partial class ServerCoreSelectorView : UserControl
{
    public event Action<string, string, string, string>? OnCoreSelected;

    public class CoreCategory
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = "Help";
        public string DataKey { get; set; } = "";
        public List<string> Cores { get; set; } = new();
    }

    private List<CoreCategory> _categories = new();
    private string _currentSelectedCore = "";

    public ServerCoreSelectorView()
    {
        InitializeComponent();
        InitializeCategories();

        this.Loaded += async (s, e) => await FetchCategories();

        this.AttachedToVisualTree += (s, e) =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                // 初始化
                UpdateSize(topLevel);

                // 监听主窗口大小变化
                topLevel.SizeChanged += (sender, args) => UpdateSize(topLevel);
            }
        };
    }

    private void UpdateSize(TopLevel topLevel)
    {
        double parentWidth = topLevel.ClientSize.Width;
        double parentHeight = topLevel.ClientSize.Height;

        this.MaxWidth = Math.Min(1000, parentWidth * 0.9); 
        this.MaxHeight = parentHeight * 0.9;

        this.Width = double.NaN;
        this.Height = double.NaN;
    }

    private void InitializeCategories()
    {
        var config = new[] {
            new { Key = "pluginsCore", Name = "插件服务端", Icon = "PuzzleOutline", Desc = "Bukkit/Spigot/Paper" },
            new { Key = "pluginsAndModsCore_Forge", Name = "混合服务端 (Forge)", Icon = "Layers", Desc = "Forge模组 + 插件" },
            new { Key = "pluginsAndModsCore_Fabric", Name = "混合服务端 (Fabric)", Icon = "Chip", Desc = "Fabric模组 + 插件" },
            new { Key = "modsCore_Forge", Name = "模组服务端 (Forge)", Icon = "Wrench", Desc = "纯 NeoForge/Forge" },
            new { Key = "modsCore_Fabric", Name = "模组服务端 (Fabric)", Icon = "Robot", Desc = "纯 Fabric" },
            new { Key = "vanillaCore", Name = "原版服务端", Icon = "CubeOutline", Desc = "Minecraft 官方原版" },
            new { Key = "bedrockCore", Name = "基岩版服务端", Icon = "GiftOutline", Desc = "Bedrock 第三方端" },
            new { Key = "proxyCore", Name = "代理服务端", Icon = "ShareVariant", Desc = "BungeeCord / Velocity" },
        };

        foreach (var c in config)
        {
            _categories.Add(new CoreCategory { Name = c.Name, Icon = c.Icon, DataKey = c.Key, Description = c.Desc });
        }
    }

    private async Task FetchCategories()
    {
        try
        {
            var result = await MSLAPIService.GetJsonDataAsync("/query/server_classify");
            if (result.Success && result.Data is JObject dataObj)
            {
                foreach (var cat in _categories)
                {
                    if (dataObj.ContainsKey(cat.DataKey))
                    {
                        var coreArray = dataObj[cat.DataKey] as JArray;
                        if (coreArray != null)
                        {
                            cat.Cores = coreArray.Select(x => x.ToString()).ToList();
                        }
                    }
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ListCategories.ItemsSource = null;
                    ListCategories.ItemsSource = _categories;

                    if (_categories.Count > 0)
                    {
                        ListCategories.SelectedIndex = 0;
                        UpdateCoreList();
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("获取失败: " + ex.Message);
        }
    }

    private void ListCategories_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateCoreList();
    }

    private void TxtSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateCoreList();
    }

    private void UpdateCoreList()
    {
        if (ListCategories == null || ListCategories.SelectedItem is not CoreCategory category) return;

        var filter = TxtSearch.Text?.ToLower() ?? "";
        var list = string.IsNullOrEmpty(filter)
            ? category.Cores
            : category.Cores.Where(c => c.ToLower().Contains(filter)).ToList();

        ItemsCores.ItemsSource = list;

        ItemsVersions.ItemsSource = null;
        TxtSelectedCoreTitle.Text = "请选择一个核心";
        TxtVersionTip.IsVisible = true;
        _currentSelectedCore = "";
    }

    private void BtnCore_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is string coreName)
        {
            _currentSelectedCore = coreName;
            TxtSelectedCoreTitle.Text = $"{coreName} 版本列表";
            TxtVersionTip.IsVisible = false;
            ItemsVersions.ItemsSource = null;
            _ = FetchVersions(coreName);
        }
    }

    private async Task FetchVersions(string coreName)
    {
        ProgressVersions.IsVisible = true;
        try
        {
            var result = await MSLAPIService.GetJsonDataAsync($"/query/available_versions/{coreName}");
            if (result.Success)
            {
                JArray? arr = result.Data as JArray;
                JObject? dataObj = null;

                if (arr != null && arr.Count > 0)
                    dataObj = arr[0] as JObject;
                else
                    dataObj = result.Data as JObject;

                if (dataObj != null && dataObj.ContainsKey("versionList"))
                {
                    var vList = dataObj["versionList"]?.ToObject<List<string>>();
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ItemsVersions.ItemsSource = vList;
                    });
                }
            }
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => ProgressVersions.IsVisible = false);
        }
    }

    private async void BtnVersion_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentSelectedCore)) return;

        if (sender is Button btn && btn.DataContext is string version)
        {
            MaskLoading.IsVisible = true;
            try
            {
                var result = await MSLAPIService.GetJsonDataAsync($"/download/server/{_currentSelectedCore}/{version}");
                if (result.Success && result.Data is JObject data)
                {
                    string url = data["url"]?.ToString() ?? "";
                    string sha256 = data["sha256"]?.ToString() ?? "";
                    string filename = $"{_currentSelectedCore}-{version}.jar";

                    OnCoreSelected?.Invoke(url, sha256, filename, _currentSelectedCore);
                    
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("获取失败: " + ex.Message);
            }
            finally
            {
                MaskLoading.IsVisible = false;
            }
        }
    }
}