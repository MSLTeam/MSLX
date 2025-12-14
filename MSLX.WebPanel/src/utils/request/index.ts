// axios配置  可自行根据项目进行更改，只需更改该文件即可，其他文件可以不动
import isString from 'lodash/isString';
import merge from 'lodash/merge';
import type { AxiosTransform, CreateAxiosOptions } from './AxiosTransform';
import { VAxios } from './Axios';
import { joinTimestamp, formatRequestDate, setObjToUrlParams } from './utils';
import { TOKEN_NAME, BASE_URL_NAME } from '@/config/global';

// 数据处理，方便区分多种处理方式
const transform: AxiosTransform = {
  // 处理请求数据
  transformRequestHook: (res, options) => {
    const { isTransformResponse, isReturnNativeResponse } = options;

    // 如果204无内容直接返回
    const method = res.config.method?.toLowerCase();
    if (res.status === 204 || method === 'put' || method === 'patch') {
      return res;
    }

    // 是否返回原生响应头 比如：需要获取响应头时使用该属性
    if (isReturnNativeResponse) {
      return res;
    }
    // 不进行任何处理，直接返回
    if (!isTransformResponse) {
      return res.data;
    }

    // 错误的时候返回
    const { data } = res;
    if (!data) {
      throw new Error('请求接口错误');
    }

    // 智能识别逻辑

    // 检查响应数据是否是项目期望的 { code, message } 标准结构
    const hasStandardStructure = data &&
      Reflect.has(data, 'code') &&
      Reflect.has(data, 'message');

    if (hasStandardStructure) {
      // 标准结构
      const { code, message } = data as any;
      const hasSuccess = code === 200;
      if (hasSuccess) {
        return data.data; // 返回标准结构中的 data 字段
      }
      throw new Error(message || `请求接口错误, 错误码: ${code}`);

    } else {
      // 2不是标准结构：
      return data;
    }

  },

  // 请求前处理配置
  beforeRequestHook: (config, options) => {
    const { apiUrl, isJoinPrefix, urlPrefix, joinParamsToUrl, formatDate, joinTime = true } = options;

    // 添加接口前缀
    if (isJoinPrefix && urlPrefix && isString(urlPrefix)) {
      config.url = `${urlPrefix}${config.url}`;
    }

    // 将baseUrl拼接
    if (apiUrl && isString(apiUrl) && !config.baseURL) {
      config.url = `${apiUrl}${config.url}`;
    }
    const params = config.params || {};
    const data = config.data || false;

    if (formatDate && data && !isString(data)) {
      formatRequestDate(data);
    }
    if (config.method?.toUpperCase() === 'GET') {
      if (!isString(params)) {
        config.params = Object.assign(params || {}, joinTimestamp(joinTime, false));
      } else {
        config.url = `${config.url + params}${joinTimestamp(joinTime, true)}`;
        config.params = undefined;
      }
    } else if (!isString(params)) {
      if (formatDate) {
        formatRequestDate(params);
      }
      if (
        Reflect.has(config, 'data') &&
        config.data &&
        (Object.keys(config.data).length > 0 || data instanceof FormData)
      ) {
        config.data = data;
        config.params = params;
      } else {
        config.data = params;
        config.params = undefined;
      }
      if (joinParamsToUrl) {
        config.url = setObjToUrlParams(config.url as string, { ...config.params, ...config.data });
      }
    } else {
      config.url += params;
      config.params = undefined;
    }
    return config;
  },

  // 请求拦截器处理
  requestInterceptors: (config, _options) => {
    const token = localStorage.getItem(TOKEN_NAME);
    const baseUrl = localStorage.getItem(BASE_URL_NAME);

    // 动态设置 baseURL
    if (baseUrl && !/^(https?:)?\/\//.test(config.url || '') && !config.baseURL) {
      config.baseURL = baseUrl;
    }

    // 动态设置 x-user-token
    if (token && (config as Recordable)?.requestOptions?.withToken !== false) {
      // 如果包含了headers 大概率不是请求到守护进程端的 就不传递apikey了
      if (!config.headers.hasAuthorization()) {
        (config as Recordable).headers['x-user-token'] = token;
      }
    }
    return config;
  },

  // 响应拦截器处理
  responseInterceptors: (res) => {
    return res;
  },

  // 响应错误处理
  responseInterceptorsCatch: (error: any) => {
    const {  response } = error;

    // 优先处理有 JSON 响应的错误 (如 401, 403, 500)
    if (response && response.data) {
      const data = response.data;
      const message = (data as any)?.message;

      if (message) {
        // 如果后端 JSON 中有 message, 抛出这个 message
        return Promise.reject(new Error(message));
      }
      // 如果有 JSON 但没有 'message'，也立即 reject, 不重试
      return Promise.reject(error);
    }

    // 只有在没有 JSON 响应时 (如网络错误/超时) 才尝试重试
    /*
    if (config && config.requestOptions.retry) {
      config.retryCount = config.retryCount || 0;

      if (config.retryCount < config.requestOptions.retry.count) {
        config.retryCount += 1;

        const backoff = new Promise((resolve) => {
          setTimeout(() => {
            resolve(config);
          }, config.requestOptions.retry.delay || 1);
        });
        config.headers = { ...config.headers, 'Content-Type': 'application/json;charset=UTF-8' };
        return backoff.then((config) => request.request(config));
      }
    }*/

    // 重试失败或不重试的网络错误
    return Promise.reject(error);
  },
};

function createAxios(opt?: Partial<CreateAxiosOptions>) {
  return new VAxios(
    merge(
      <CreateAxiosOptions>{
        authenticationScheme: '',
        timeout: 10 * 1000,
        withCredentials: false,
        headers: { 'Content-Type': 'application/json;charset=UTF-8' },
        transform,
        requestOptions: {
          apiUrl: '',
          isJoinPrefix: true,
          urlPrefix: '',
          isReturnNativeResponse: false,
          isTransformResponse: true,
          joinParamsToUrl: false,
          formatDate: true,
          joinTime: false,
          ignoreRepeatRequest: true,
          withToken: true,
          retry: {
            count: 3,
            delay: 1000,
          },
        },
      },
      opt || {},
    ),
  );
}
export const request = createAxios();
