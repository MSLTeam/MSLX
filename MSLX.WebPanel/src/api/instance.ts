import { request } from '@/utils/request';
import {
  AllInstanceBackupFilesModel,
  BannedIpItem,
  BannedPlayerItem,
  CreateInstanceQucikModeModel,
  InstanceBackupFilesModel,
  InstanceInfoModel,
  InstanceListModel,
  InstanceSettingsModel,
  OpItem,
  UpdateInstanceResponseModel,
  UserCacheItem,
  WhitelistItem,
} from '@/api/model/instance';
import { useUserStore } from '@/store';

const userStore = useUserStore();

export async function postCreateInstanceQuickMode(data:CreateInstanceQucikModeModel){
  return await request.post({
    url: '/api/instance/createServer',
    data: data,
  });
}

export async function postDeleteInstance(id:number,deleteFiles:boolean = false) {
  return await request.post({
    url: '/api/instance/delete',
    data:{
      id,
      deleteFiles
    }
  });
}

export async function getInstanceList() {
  return await request.get<InstanceListModel[]>({
    url: '/api/instance/list',
  });
}

export async function postInstanceAction(id:number, action:string){
  return await request.post({
    url: '/api/instance/action',
    data:{
      id: id,
      action: action
    },
    timeout: 60 * 1000, // 可能存在小聪明开了又关 导致关闭操作耗时。
  });
}

export async function getInstanceInfo(id:number){
  return await request.get<InstanceInfoModel>({
    url: '/api/instance/info',
    params:{
      id: id
    }
  });
}

export async function getInstanceSettings(id:number){
  return await request.get<InstanceSettingsModel>({
    url: `/api/instance/settings/general/${id}`,
  });
}

export async function postInstanceSettings(data:InstanceSettingsModel){
  return await request.post<UpdateInstanceResponseModel>({
    url: `/api/instance/settings/general/${data.id}`,
    data: data
  });
}

export async function getInstanceBackupFiles(id:number){
  return await request.get<InstanceBackupFilesModel[]>({
    url: `/api/instance/backups/${id}`,
  })
}

export async function getAllInstanceBackupFiles() {
  return await request.get<AllInstanceBackupFilesModel[]>({
    url: `/api/instance/backups/all`,
  });
}

export async function postDeleteBackupFiles(id:number,fileName:string){
  return await request.post({
    url: `/api/instance/backups/delete`,
    data: {id,fileName},
  })
}
export function getBackupDownloadUrl(id: number, fileName: string) {
  const { baseUrl, token } = userStore;
  return `${baseUrl || window.location.origin}/api/instance/backups/download?id=${id}&fileName=${encodeURIComponent(fileName)}&x-user-token=${token}`;
}

// 玩家管理相关
// 在线玩家
export async function getOnlinePlayers(id: number) {
  return await request.get<string[]>({
    url: `/api/instance/players/online/${id}`,
  });
}

// 白名单
export async function getWhitelist(id: number) {
  return await request.get<WhitelistItem[]>({ url: `/api/instance/players/whitelist/${id}` });
}
export async function addWhitelist(id: number, name: string) {
  return await request.post({ url: `/api/instance/players/whitelist/add/${id}`, data: { name } });
}
export async function removeWhitelist(id: number, name: string) {
  return await request.post({ url: `/api/instance/players/whitelist/remove/${id}`, data: { name } });
}

// 管理员 (OP)
export async function getOps(id: number) {
  return await request.get<OpItem[]>({ url: `/api/instance/players/ops/${id}` });
}
export async function addOp(id: number, name: string) {
  return await request.post({ url: `/api/instance/players/ops/add/${id}`, data: { name } });
}
export async function removeOp(id: number, name: string) {
  return await request.post({ url: `/api/instance/players/ops/remove/${id}`, data: { name } });
}

// 封禁玩家
export async function getBannedPlayers(id: number) {
  return await request.get<BannedPlayerItem[]>({ url: `/api/instance/players/banplayer/${id}` });
}
export async function addBannedPlayer(id: number, name: string, reason?: string) {
  return await request.post({ url: `/api/instance/players/banplayer/add/${id}`, data: { name, reason } });
}
export async function removeBannedPlayer(id: number, name: string) {
  return await request.post({ url: `/api/instance/players/banplayer/remove/${id}`, data: { name } });
}

// 封禁 IP
export async function getBannedIps(id: number) {
  return await request.get<BannedIpItem[]>({ url: `/api/instance/players/banip/${id}` });
}
export async function addBannedIp(id: number, ip: string, reason?: string) {
  return await request.post({ url: `/api/instance/players/banip/add/${id}`, data: { ip, reason } });
}
export async function removeBannedIp(id: number, ip: string) {
  return await request.post({ url: `/api/instance/players/banip/remove/${id}`, data: { ip } });
}

// 历史玩家
export async function getHistoryPlayers(id: number) {
  return await request.get<UserCacheItem[]>({ url: `/api/instance/players/history/${id}` });
}

// 获取世界出生点
export async function getWorldSpawn(id: number) {
  return await request.get<{ x: number; z: number }>({
    url: `/api/instance/map/spawn/${id}`,
  });
}
