export interface UserInfoModel {
  id: string;
  username: string;
  name: string;
  avatar: string;
  role: string;
  apiKey: string;
  lastLoginTime: string;
  openMSLID?: string;
}

export interface UpdateUserRequest {
  username?: string;
  name?: string;
  avatar?: string;
  password?: string;
  resetApiKey?: boolean;
}

// 用户数据模型
export interface AdminUserDto {
  id: string;
  username: string;
  name: string;
  avatar: string;
  role: 'admin' | 'user';
  apiKey: string;
  lastLoginTime?: string;
}

// 创建用户请求
export interface CreateUserRequest {
  username: string;
  password: string;
  name?: string;
  role: string;
}

// 更新用户请求
export interface UpdateUserRequest {
  name?: string;
  avatar?: string;
  password?: string; // 为空不修改
  role?: string;
  resetApiKey?: boolean;
}
