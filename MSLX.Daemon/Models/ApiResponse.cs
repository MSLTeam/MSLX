namespace MSLX.Daemon.Models
{
    public class ApiResponse<T>
    {
        public int code { get; set; } = 200;
        public string message { get; set; } = "";
        public T? data { get; set; }
    }
}
