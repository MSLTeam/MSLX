import { request } from '@/utils/request';
import { CronTaskItemModel } from '@/api/model/cronTasks';

export async function getCronTasks(instanceId: number) {
  return await request.get<CronTaskItemModel[]>({
    url: `/api/instance/tasks/list/${instanceId}`,
  });
}

export async function addCronTask(instanceId: number, name: string, cron: string, payload: string,type:string,enable: boolean) {
  return await request.post({
    url: `/api/instance/tasks/create`,
    data: { instanceId, name, cron, payload,type,enable },
  });
}

export async function updateCronTask(instanceId: number, id: string, name: string, cron: string, payload: string,type:string,enable: boolean) {
  return await request.post({
    url: `/api/instance/tasks/update`,
    data: { instanceId, id, name, cron, payload,type,enable },
  });
}

export async function deleteCronTask(id: string) {
  return await request.post({
    url: `/api/instance/tasks/delete${id}`,
  });
}
