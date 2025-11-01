namespace MSLX.Daemon.Models
{
    public class ApiResponse<T>
    {
        public int Code { get; set; } = 200;
        public string Message { get; set; } = "";
        public T? Data { get; set; }
    }

    public class ApiResponseNoData<T>
    {
        public int Code { get; set; } = 200;
        public string Message { get; set; } = "";
    }
}
