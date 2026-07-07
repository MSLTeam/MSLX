import { PythonInfoModel } from '@/api/model/instance';
import { request } from '@/utils/request';

export async function getPythonList(force: boolean = false) {
  return await request.get<PythonInfoModel[]>({
    url: '/api/python/list',
    params: {
      refresh: force,
    },
    timeout: 60 * 1000, // 扫描 + 检测 MCDR 可能较慢，延长超时
  });
}

export async function inspectPython(python: string) {
  return await request.get<PythonInfoModel>({
    url: '/api/python/inspect',
    params: {
      python,
    },
    timeout: 30 * 1000,
  });
}
