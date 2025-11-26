import { postCreateFrpTunnel } from '@/api/frp';
import { changeUrl } from '@/router';
import { useTunnelsStore } from '@/store/modules/frp';
import { MessagePlugin } from 'tdesign-vue-next';

const tunnelsStore = useTunnelsStore();

export async function createFrpTunnel(name:string,config:string,provider:string,format:string = 'toml'){
  await postCreateFrpTunnel(name,config,provider,format);
  MessagePlugin.success('添加成功');
  await tunnelsStore.getTunnels();
  changeUrl('/frp/list');
}
