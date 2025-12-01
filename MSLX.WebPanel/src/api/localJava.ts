import { LocalJavaListModel } from '@/api/model/localJava';
import { request } from '@/utils/request';

export async function getLocalJavaList(force:boolean = false){
  return await request.get<LocalJavaListModel[]>({
    url: '/api/java/list',
    params:{
      refresh: force
    },
    timeout: 60 * 1000, // 原本拦截器设置的10秒可能不够用 这里延长亿点点
  });
}
