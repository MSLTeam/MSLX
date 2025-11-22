using MSLX.Daemon.Models;

namespace MSLX.Daemon.Utils
{
    public class ApiResponseService
    {
        /// <summary>
        /// Create response without data
        /// </summary>
        /// <returns>ApiResponse</returns>
        public static ApiResponse<object> CreateResponse(int code, string message)
        {
            return new ApiResponse<object>
            {
                Code = code,
                Message = message,
                Data = null
            };
        }

        /// <summary>
        /// Create response with data
        /// </summary>
        /// <returns>ApiResponse</returns>
        public static ApiResponse<T> CreateResponse<T>(int code, string message, T data)
        {
            return new ApiResponse<T>
            {
                Code = code,
                Message = message,
                Data = data
            };
        }

        /// <summary>
        /// 200 OK NO DATA
        /// </summary>
        public static ApiResponse<object> Success(string message = "OK")
        {
            return CreateResponse(200, message);
        }

        /// <summary>
        /// 200 OK WITH DATA
        /// </summary>
        public static ApiResponse<T> Success<T>(T data, string message = "OK")
        {
            return CreateResponse(200, message, data);
        }

        /// <summary>
        /// ERROR 4XX
        /// </summary>
        public static ApiResponse<object> Error(string message, int code = 400)
        {
            return CreateResponse(code, message);
        }

        /// <summary>
        /// 400 BAD REQUEST
        /// </summary>
        public static ApiResponse<object> BadRequest(string message)
        {
            return CreateResponse(400, message);
        }

        /// <summary>
        /// 404 NOT FOUND
        /// </summary>
        public static ApiResponse<object> NotFound(string message = "NOTFOUND")
        {
            return CreateResponse(404, message);
        }

        /// <summary>
        /// 422 VALIDATION ERROR
        /// </summary>
        public static ApiResponse<object> ValidationError(string message)
        {
            return CreateResponse(422, message);
        }
    }
}