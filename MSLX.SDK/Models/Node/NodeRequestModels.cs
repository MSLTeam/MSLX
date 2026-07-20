using System.ComponentModel.DataAnnotations;

namespace MSLX.SDK.Models.Node
{
    public class LinkNodeRequest
    {
        [Required(ErrorMessage = "节点地址不能为空")]
        public string NodeUrl { get; set; } = string.Empty;

        public string? NodeLogo { get; set; } = "https://www.mslmc.cn/logo.png";

        public string? MasterUrl { get; set; } = string.Empty;

        [Required(ErrorMessage = "节点名称不能为空")]
        public string NodeName { get; set; } = string.Empty;

        [Required(ErrorMessage = "通讯秘钥不能为空")]
        public string LinkKey { get; set; } = string.Empty;

        public string? NodeTags { get; set; } = string.Empty;
    }

    public class VerifyTokenRequest
    {
        [Required(ErrorMessage = "Token不能为空")]
        public string Token { get; set; } = string.Empty;
    }

    public class AcceptLinkRequest
    {
        [Required(ErrorMessage = "主节点标识不能为空")]
        public string MasterId { get; set; } = string.Empty;

        [Required(ErrorMessage = "主节点地址不能为空")]
        public string MasterUrl { get; set; } = string.Empty;

        [Required(ErrorMessage = "通讯秘钥不能为空")]
        public string LinkKey { get; set; } = string.Empty;
    }

    public class UpdateLinkRequest
    {
        [Required(ErrorMessage = "主节点地址不能为空")]
        public string MasterUrl { get; set; } = string.Empty;

        [Required(ErrorMessage = "通信秘钥不能为空")]
        public string CommsKey { get; set; } = string.Empty;

        [Required(ErrorMessage = "节点标识不能为空")]
        public string NodeId { get; set; } = string.Empty;
    }
}
