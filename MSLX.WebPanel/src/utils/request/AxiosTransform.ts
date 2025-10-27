import type { AxiosRequestConfig, InternalAxiosRequestConfig, AxiosResponse } from 'axios';
import { AxiosError } from 'axios';
import type { RequestOptions, Result } from '@/types/axios';

export interface CreateAxiosOptions extends AxiosRequestConfig {
  // https://developer.mozilla.org/en-US/docs/Web/HTTP/Authentication#authentication_schemes
  authenticationScheme?: string;
  // 数据处理
  transform?: AxiosTransform;
  // 请求选项
  requestOptions?: RequestOptions;
}

export abstract class AxiosTransform {
  // 请求前Hook
  beforeRequestHook?: (_config: InternalAxiosRequestConfig, _options: RequestOptions) => InternalAxiosRequestConfig;

  // 转换前Hook
  transformRequestHook?: (_res: AxiosResponse<Result>, _options: RequestOptions) => any;

  // 请求失败处理
  requestCatchHook?: (_e: Error | AxiosError, _options: RequestOptions) => Promise<any>;

  // 请求前的拦截器
  requestInterceptors?: (_config: InternalAxiosRequestConfig, _options: CreateAxiosOptions) => InternalAxiosRequestConfig;

  // 请求后的拦截器
  responseInterceptors?: (_res: AxiosResponse) => AxiosResponse;

  // 请求前的拦截器错误处理
  requestInterceptorsCatch?: (_error: AxiosError) => void;

  // 请求后的拦截器错误处理
  responseInterceptorsCatch?: (_error: AxiosError) => void;
}
