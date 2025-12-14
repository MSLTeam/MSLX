import { request } from '@/utils/request';
import { AdminUserDto, CreateUserRequest, UpdateUserRequest, UserInfoModel } from '@/api/model/user';

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



// 获取用户列表
export function getUserList() {
  return request.get<AdminUserDto[]>({
    url: '/api/admin/user/list',
  });
}

// 创建用户
export function createUser(data: CreateUserRequest) {
  return request.post({
    url: '/api/admin/user/create',
    data,
  });
}

// 更新用户
export function updateUser(id: string, data: UpdateUserRequest) {
  return request.post({
    url: `/api/admin/user/update/${id}`,
    data,
  });
}

// 删除用户
export function deleteUser(id: string) {
  return request.post({
    url: `/api/admin/user/delete/${id}`,
  });
}
