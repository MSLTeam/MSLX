import { request } from '@/utils/request';
import {
  ServerCoreClassifyModel,
  ServerCoreDownloadInfoModel,
  ServerCoreGameVersionModel,
} from '@/api/mslapi/model/serverCore';

export async function getServerCoreClassify(){
  return await request.get<ServerCoreClassifyModel[]>({
    url: '/mirrors',
    baseURL: 'https://api.mslmc.cn/v4'
  });
}

export async function getServerCoreGameVersion(name:string){
  return await request.get<ServerCoreGameVersionModel>({
    url: `/mirrors/${name}`,
    baseURL: 'https://api.mslmc.cn/v4',
  });
}

export async function getServerCoreBuilds(name:string, version:string){
  return await request.get<string[]>({
    url: `/mirrors/${name}/${version}`,
    baseURL: 'https://api.mslmc.cn/v4',
  });
}

export async function getServerCoreDownloadInfo(name:string, version:string, build:string = 'latest'){
  return await request.get<ServerCoreDownloadInfoModel>({
    url: `/download/server/${name}/${version}?build=${build}`,
    baseURL: 'https://api.mslmc.cn/v4'
  });
}
