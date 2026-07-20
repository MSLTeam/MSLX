import { AxiosRequestConfig } from 'axios';

export interface RequestOptions {
  apiUrl?: string;
  isJoinPrefix?: boolean;
  urlPrefix?: string;
  joinParamsToUrl?: boolean;
  formatDate?: boolean;
  isTransformResponse?: boolean;
  isReturnNativeResponse?: boolean;
  ignoreRepeatRequest?: boolean;
  joinTime?: boolean;
  withToken?: boolean;
  requestToSlaveNode?: boolean | 'auto'; // 是否发送到子节点 auto模式为自动正则判定
  retry?: {
    count: number;
    delay: number;
  };
}

export interface Result<T = any> {
  code: number;
  data: T;
}

export interface AxiosRequestConfigRetry extends AxiosRequestConfig {
  retryCount?: number;
}
