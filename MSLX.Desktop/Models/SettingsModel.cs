using MSLX.SDK.Models.Settings;

namespace MSLX.Desktop.Models
{
    // 用于下拉框的选项类
    public class MirrorOption
    {
        public required string Label { get; set; }
        public required string Value { get; set; }

        public override string ToString() => Label;
    }
}