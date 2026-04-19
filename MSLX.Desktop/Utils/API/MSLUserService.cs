using MSLX.Desktop.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MSLX.Desktop.Utils.API
{
    internal class MSLUserService
    {
        public static string ApiUrl { get; } = "https://user.mslmc.net/api";

        /// <summary>
        /// 普通ApiGet请求
        /// </summary>
        /// <param name="path">路径，如“/notice”</param>
        /// <param name="queryParameters">query参数，可直接加在路径后面“?query=md”，也可在此通过Dictionary进行设置</param>
        /// <returns>Httpservice.HttpResponse</returns>
        public async static Task<HttpResponse> GetAsync(string path, Dictionary<string, string>? queryParameters, Action<HttpRequestHeaders>? configureHeaders = null)
        {
            // 确保路径以 "/" 开头
            if (!path.StartsWith("/"))
                path = "/" + path;
            return await HttpService.GetAsync(
                ApiUrl + path,
                queryParameters,
                configureHeaders,
                uaType: UAManager.UAType.MSLX);
        }

        /// <summary>
        /// 普通ApiPost请求
        /// </summary>
        /// <param name="path">路径，如“/notice”</param>
        /// <param name="postContentType">发送数据类型，如：HttpMethod.Post</param>
        /// <param name="data">发送数据</param>
        /// <param name="headers">请求头</param>
        /// <returns>Httpservice.HttpResponse</returns>
        public async static Task<HttpResponse> PostAsync(string path, HttpService.PostContentType postContentType, object data, Dictionary<string, string>? headers = null)
        {
            // 确保路径以 "/" 开头
            if (!path.StartsWith("/"))
                path = "/" + path;
            return await HttpService.PostAsync(
                ApiUrl + path,
                null,
                postContentType,
                data,
                headers != null ? reqHeaders =>
                {
                    foreach (var header in headers)
                        reqHeaders.TryAddWithoutValidation(header.Key, header.Value);
                } : null,
                UAManager.UAType.MSLX);
        }
    }
}
