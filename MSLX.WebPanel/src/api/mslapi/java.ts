import { request } from '@/utils/request';

export async function getJavaVersionList(os:string,arch:string){
  return await request.get<string[]>({
    url: '/query/jdk',
    baseURL: 'https://api.mslmc.cn/v3',
    params:{
      os,
      arch
    }
  });
}
