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

export async function postCreateInstanceQuickMode(data:CreateInstanceQucikModeModel){
  return await request.post({
    url: '/api/instance/createServer',
    data: data,
  });
}

export async function postCancelCreateInstance(serverId:string, cleanupFiles:boolean = false) {
  return await request.post({
    url: '/api/instance/cancelCreation',
    data: {
      serverId,
      cleanupFiles
    }
  });
}

// 带确认对话框的取消创建
export async function cancelCreationWithConfirm(serverId: string): Promise<boolean> {
  const { DialogPlugin, MessagePlugin, Checkbox } = await import('tdesign-vue-next');
  const { h, ref } = await import('vue');

  const cleanupFiles = ref(false);

  const confirmed = await new Promise<boolean>((resolve) => {
    const dialog = DialogPlugin({
      header: '取消部署',
      theme: 'warning',
      confirmBtn: {
        content: '确认取消',
        theme: 'primary',
      },
      cancelBtn: {
        content: '继续部署',
        theme: 'default',
      },
      body: () =>
        h('div', { style: 'display: flex; flex-direction: column; gap: 12px; padding: 4px 0;' }, [
          h('p', { style: 'margin: 0; font-size: 14px; color: var(--td-text-color-primary);' }, '确定要取消当前服务器实例的部署任务吗？'),
          h(
            Checkbox,
            {
              checked: cleanupFiles.value,
              'onUpdate:checked': (val: boolean) => {
                cleanupFiles.value = val;
              },
              onChange: (val: boolean) => {
                cleanupFiles.value = val;
              },
            },
            () =>
              h(
                'span',
                {
                  style: 'color: var(--td-error-color, #e34d59); font-size: 13px;',
                },
                '同时清理已下载的实例文件 (删除实例文件夹及文件)',
              ),
          ),
        ]),
      onConfirm: () => {
        dialog.destroy();
        resolve(true);
      },
      onCancel: () => {
        dialog.destroy();
        resolve(false);
      },
      onClose: () => {
        dialog.destroy();
        resolve(false);
      },
    });
  });

  if (!confirmed) {
    return false; // 用户放弃取消或关闭了弹窗
  }

  await postCancelCreateInstance(serverId, cleanupFiles.value);
  MessagePlugin.success(cleanupFiles.value ? '已取消部署并清理文件' : '已取消部署，文件已保留');
  return true;
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
  const userStore = useUserStore();
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

// 导出服务端包
export async function exportInstancePack(id: number, excludes: string[]) {
  return await request.post<{ taskId: string }>({
    url: `/api/instance/${id}/export`,
    data: {
      excludes: excludes
    },
    timeout: 10 * 1000 // 获取任务id应该很快
  });
}
