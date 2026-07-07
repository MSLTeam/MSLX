using MSLX.Daemon.Utils.McdrConfig;

namespace MSLX.Tests;

public class McdrConfigGeneratorTests
{
    [Theory]
    [InlineData("paper-1.20.4.jar", "bukkit_handler")]
    [InlineData("purpur-1.21.jar", "bukkit_handler")]
    [InlineData("folia-1.20.jar", "bukkit_handler")]
    [InlineData("leaves-1.20.jar", "bukkit_handler")]
    [InlineData("Mohist-1.20.1.jar", "bukkit_handler")]
    [InlineData("spigot-1.19.jar", "bukkit_handler")]
    [InlineData("forge-1.20.1-installer.jar", "forge_handler")]
    [InlineData("neoforge-21.1.100.jar", "forge_handler")]
    [InlineData("arclight-forge-1.20.jar", "arclight_handler")]
    [InlineData("catserver-1.18.jar", "cat_server_handler")]
    [InlineData("fabric-server-launch.jar", "vanilla_handler")]
    [InlineData("quilt-server.jar", "vanilla_handler")]
    [InlineData("minecraft_server.1.20.jar", "vanilla_handler")]
    [InlineData("velocity-3.3.jar", "velocity_handler")]
    [InlineData("waterfall-1.20.jar", "waterfall_handler")]
    [InlineData("BungeeCord.jar", "bungeecord_handler")]
    [InlineData(null, "vanilla_handler")]
    [InlineData("", "vanilla_handler")]
    public void InferHandler_maps_core_name_to_handler(string? coreName, string expected)
    {
        Assert.Equal(expected, McdrConfigGenerator.InferHandler(coreName));
    }

    [Fact]
    public void InferHandler_prefers_arclight_over_forge()
    {
        // arclight 核心名可能包含 forge 字样，但应识别为 arclight_handler
        Assert.Equal("arclight_handler", McdrConfigGenerator.InferHandler("arclight-forge-1.20.1.jar"));
    }

    [Fact]
    public void BuildStartCommand_produces_jar_command()
    {
        var cmd = McdrConfigGenerator.BuildStartCommand("java", 1024, 2048, null, "server.jar");
        Assert.Equal("java -Xms1024M -Xmx2048M -jar server.jar nogui", cmd);
    }

    [Fact]
    public void BuildStartCommand_includes_extra_args()
    {
        var cmd = McdrConfigGenerator.BuildStartCommand("java", 1024, 2048, "-XX:+UseG1GC", "server.jar");
        Assert.Equal("java -Xms1024M -Xmx2048M -XX:+UseG1GC -jar server.jar nogui", cmd);
    }

    [Fact]
    public void BuildStartCommand_quotes_java_path_with_spaces()
    {
        var cmd = McdrConfigGenerator.BuildStartCommand(@"C:\Program Files\Java\bin\java.exe", 512, 1024, null,
            "server.jar");
        Assert.StartsWith("\"C:\\Program Files\\Java\\bin\\java.exe\"", cmd);
        Assert.EndsWith("-jar server.jar nogui", cmd);
    }

    [Fact]
    public void BuildStartCommand_defaults_missing_core_to_server_jar()
    {
        var cmd = McdrConfigGenerator.BuildStartCommand("java", null, null, null, null);
        Assert.Equal("java -jar server.jar nogui", cmd);
    }

    [Fact]
    public void BuildStartCommand_handles_forge_libraries_args()
    {
        var cmd = McdrConfigGenerator.BuildStartCommand("java", 1024, 2048, null, "@libraries/net/neoforged/args.txt");
        Assert.Equal("java -Xms1024M -Xmx2048M @libraries/net/neoforged/args.txt nogui", cmd);
        Assert.DoesNotContain("-jar", cmd);
    }

    [Fact]
    public void Generate_includes_key_fields()
    {
        var options = new McdrConfigOptions
        {
            WorkingDirectory = "server",
            StartCommand = "java -jar server.jar nogui",
            Handler = "bukkit_handler",
            Encoding = "utf8",
            Decoding = "utf8",
            AdvancedConsole = false
        };

        var yaml = McdrConfigGenerator.Generate(options);

        Assert.Contains("working_directory: server", yaml);
        Assert.Contains("handler: bukkit_handler", yaml);
        Assert.Contains("start_command: 'java -jar server.jar nogui'", yaml);
        Assert.Contains("encoding: utf8", yaml);
        Assert.Contains("decoding: utf8", yaml);
        // MSLX 重定向 stdio，必须关闭高级控制台
        Assert.Contains("advanced_console: false", yaml);
    }

    [Fact]
    public void Generate_single_quotes_windows_path_in_start_command()
    {
        var options = new McdrConfigOptions
        {
            StartCommand = @"""C:\Program Files\Java\bin\java.exe"" -jar server.jar nogui",
            Handler = "vanilla_handler"
        };

        var yaml = McdrConfigGenerator.Generate(options);

        // 含冒号/反斜杠的启动命令应被单引号包裹，避免 YAML 解析歧义
        Assert.Contains(@"start_command: '""C:\Program Files\Java\bin\java.exe"" -jar server.jar nogui'", yaml);
    }

    [Fact]
    public void Generate_writes_pip_mirror_when_provided()
    {
        var options = new McdrConfigOptions
        {
            Handler = "vanilla_handler",
            PluginPipInstallExtraArgs = "-i https://pypi.tuna.tsinghua.edu.cn/simple"
        };

        var yaml = McdrConfigGenerator.Generate(options);

        Assert.Contains("plugin_pip_install_extra_args: '-i https://pypi.tuna.tsinghua.edu.cn/simple'", yaml);
    }

    [Fact]
    public void Generate_leaves_pip_blank_when_not_provided()
    {
        var options = new McdrConfigOptions { Handler = "vanilla_handler" };

        var yaml = McdrConfigGenerator.Generate(options);

        Assert.Contains("plugin_pip_install_extra_args:\n", yaml + "\n");
        Assert.DoesNotContain("plugin_pip_install_extra_args: '", yaml);
    }
}
