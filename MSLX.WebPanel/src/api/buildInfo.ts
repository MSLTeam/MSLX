import { BuildInfoModel } from '@/api/model/buildInfo';
import { request } from '@/utils/request';

export async function getBuildInfo() {
  return await request.get<BuildInfoModel>({
    url: '/build.json',
    baseURL: '/',
    params: {
      t: Date.now()
    }
  });
}
