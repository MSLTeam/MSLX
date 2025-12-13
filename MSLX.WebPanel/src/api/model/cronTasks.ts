export interface CronTaskItemModel{
  id: string;
  name: string;
  cron: string;
  payload: string;
  enable: boolean;
  lastRunTime: string;
  instanceId: number;
  type:string;
}
