import { request } from '@/utils/request';
import { P2PServerModel } from '@/api/mslapi/model/p2p';

export async function getP2PServerList() {
  return await request.get<P2PServerModel>({
    url: '/query/p2p_server',
    baseURL: 'https://api.mslmc.cn/v3'
  });
}
