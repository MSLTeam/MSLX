import { request } from '@/utils/request';
import {
  ServerCoreClassifyModel,
  ServerCoreDownloadInfoModel,
  ServerCoreGameVersionModel,
} from '@/api/mslapi/model/serverCore';

export async function getServerCoreClassify(){
  return await request.get<ServerCoreClassifyModel[]>({
    url: '/query/server_classify',
    baseURL: 'https://api.mslmc.cn/v3'
  });
}

export async function getServerCoreGameVersion(name:string){
  return await request.get<ServerCoreGameVersionModel[]>({
    url: `/query/available_versions/${name}`,
    baseURL: 'https://api.mslmc.cn/v3',
  });
}

export async function getServerCoreDownloadInfo(name:string, version:string){
  return await request.get<ServerCoreDownloadInfoModel>({
    url: `/download/server/${name}/${version}`,
    baseURL: 'https://api.mslmc.cn/v3'
  });
}
