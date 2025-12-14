export interface UserInfoModel {
  id: string;
  username: string;
  name: string;
  avatar: string;
  role: string;
  apiKey: string;
  lastLoginTime: string;
}

export interface UpdateUserRequest {
  username?: string;
  name?: string;
  avatar?: string;
  password?: string;
  resetApiKey?: boolean;
}
