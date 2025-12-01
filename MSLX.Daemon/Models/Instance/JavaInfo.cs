namespace MSLX.Daemon.Models.Instance;

public class JavaInfo
{
        public string Path { get; set; }      // java 可执行文件的绝对路径
        public string Home { get; set; }      // JAVA_HOME 路径
        public string Version { get; set; }   // 版本号
        public string Vendor { get; set; }    // 供应商
        public bool Is64Bit { get; set; }     // 是否64位
}