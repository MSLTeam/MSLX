import { request } from '@/utils/request';
import { UpdateUserRequest, UserInfoModel } from '@/api/model/user';

// 获取当前用户信息
export function getSelfInfo() {
  return request.get<UserInfoModel>({
    url: '/api/user/me',
  });
}

// 更新当前用户信息
export function updateSelfInfo(data: UpdateUserRequest) {
  return request.post({
    url: '/api/user/me/update',
    data,
  });
}
