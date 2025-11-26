import { request } from '@/utils/request';
import { FrpListModel } from '@/api/model/frp';

export function getFrpList() {
  return request.get<FrpListModel[]>({
    url: '/api/frp/list',
  });
}
