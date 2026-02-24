namespace MSLX.Desktop.Models
{
    public class SettingsModel
    {
        public bool FireWallBanLocalAddr { get; set; }
        public bool OpenWebConsoleOnLaunch { get; set; }
        public string NeoForgeInstallerMirrors { get; set; } = "MSL Mirrors";
        public string ListenHost { get; set; } = "localhost";
        public int ListenPort { get; set; } = 1027;

        // 以下字段在UI中隐藏，但必须在保存时回传
        public string OAuthMSLClientID { get; set; } = string.Empty;
        public string OAuthMSLClientSecret { get; set; } = string.Empty;
    }

    // 用于下拉框的选项类
    public class MirrorOption
    {
        public string Label { get; set; }
        public string Value { get; set; }

        public override string ToString() => Label;
    }
}