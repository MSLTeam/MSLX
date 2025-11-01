using MSLX.Daemon.Models;

namespace MSLX.Daemon.Utils
{
    public class ApiResponseService
    {
        public static ApiResponseNoData<T> CreateResponse<T>(int code, string message)
        {
            return new ApiResponseNoData<T>
            {
                Code = code,
                Message = message,
            };
        }

        public static ApiResponse<T> CreateResponse<T>(int code, string message, T data)
        {
            return new ApiResponse<T>
            {
                Code = code,
                Message = message,
                Data = data
            };
        }
    }
}