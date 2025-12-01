import { LocalJavaListModel } from '@/api/model/localJava';
import { request } from '@/utils/request';

export async function getLocalJavaList(){
  return await request.get<LocalJavaListModel[]>({
    url: '/api/java/list',
  });
}
