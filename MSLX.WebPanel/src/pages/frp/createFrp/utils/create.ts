import { h, ref } from 'vue';
import { postCreateFrpTunnel } from '@/api/frp';
import { changeUrl } from '@/router';
import { useTunnelsStore } from '@/store/modules/frp';
import { DialogPlugin, Input, MessagePlugin } from 'tdesign-vue-next';

const tunnelsStore = useTunnelsStore();

export async function createFrpTunnel(
  name: string,
  config: string,
  provider: string,
  format: string = 'toml',
  showCustomName = true,
) {
  let finalName = name;

  if (showCustomName) {
    try {
      finalName = await new Promise((resolve, reject) => {
        const inputValue = ref(name);

        const dialog = DialogPlugin({
          header: '自定义隧道名称',
          body: () =>
            h(Input, {
              value: inputValue.value,
              placeholder: '请输入隧道名称',
              clearable: true,
              onChange: (val: string) => {
                inputValue.value = val;
              },
            }),
          onConfirm: () => {
            if (!inputValue.value.trim()) {
              MessagePlugin.warning('隧道名称不能为空');
              return;
            }
            dialog.hide();
            resolve(inputValue.value.trim());
          },
          onClose: () => {
            dialog.hide();
            reject(new Error('cancel'));
          },
          onCancel: () => {
            dialog.hide();
            reject(new Error('cancel'));
          },
        });
      });
    } catch {
      return;
    }
  }
  await postCreateFrpTunnel(finalName, config, provider, format);
  MessagePlugin.success('添加成功');
  await tunnelsStore.getTunnels();
  changeUrl('/frp/list');
}
