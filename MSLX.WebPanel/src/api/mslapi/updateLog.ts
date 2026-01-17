import { request } from '@/utils/request';
import { UpdateLogResponseModel } from '@/api/mslapi/model/updateLog';

export async function getMSLXUpdateLog() {
  return await request.get<UpdateLogResponseModel>({
    url: '/query/changelogs?software=MSLX',
    baseURL: 'https://api.mslmc.cn/v3',
  });
}
