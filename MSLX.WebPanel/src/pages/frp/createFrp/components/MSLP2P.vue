<script setup lang="ts">
import { getP2PServerList } from '@/api/mslapi/p2p';
import { type FormProps, FormRules, MessagePlugin } from 'tdesign-vue-next';
import { createFrpTunnel } from '@/pages/frp/createFrp/utils/create';
import { reactive } from 'vue';
import { generateRandomString } from '@/utils/tools';

interface FormData {
  roomId: string;
  roomKey: string;
  bindPort: number;
  isHoster: boolean;
}

const formData = reactive<FormData>({
  roomId: '',
  roomKey: '',
  bindPort: 25565,
  isHoster: true,
});

const rules: FormRules<FormData> = {
  roomId: [{ required: true, message: '请输入房间号', type: 'error' }],
  roomKey: [{ required: true, message: '请输入房间密钥', type: 'error' }],
  bindPort: [{ required: true, message: '请输入绑定端口', type: 'error' }],
};

const onSubmit: FormProps['onSubmit'] = async ({ validateResult }) => {
  if (validateResult === true) {
    await createP2PTunnel(formData.isHoster, formData.roomId, formData.roomKey, formData.bindPort);
  } else {
    MessagePlugin.warning('请检查参数是否全部填写完成！');
  }
};

async function createP2PTunnel(isHoster: boolean, roomId: string, roomkey: string, bindPort: number) {
  try {
    const p2pserver = await getP2PServerList();
    let config;
    if (isHoster) {
      // 房主
      config =
        `serverAddr = "${p2pserver.ip}"\n` +
        `serverPort = ${p2pserver.port}\n` +
        '\n' +
        '[[proxies]]\n' +
        `name = "${roomId}"\n` +
        'type = "xtcp"\n' +
        `secretKey = "${roomkey}"\n` +
        'localIP = "127.0.0.1"\n' +
        `localPort = ${bindPort}`;
    } else {
      // 访客
      config =
        `serverAddr = "${p2pserver.ip}"\n` +
        `serverPort = ${p2pserver.port}\n` +
        '\n' +
        '[[visitors]]\n' +
        'name = "p2p_visitor"\n' +
        'type = "xtcp"\n' +
        `serverName = "${roomId}"\n` +
        `secretKey = "${roomkey}"\n` +
        'bindAddr = "127.0.0.1"\n' +
        `bindPort = ${bindPort}`;
    }
    await createFrpTunnel(isHoster ? `「联机 - 房主」${roomId}` : `「联机 - 访客」${roomId}`, config, 'MSL P2P');
  } catch (error) {
    MessagePlugin.error(`创建联机隧道失败！${error.message}`);
  }
}
</script>

<template>
  <div>
    <t-card :bordered="false">
      <template #header>
        <span style="font-weight: bold; font-size: 16px">MSL P2P 联机隧道</span>
      </template>

      <t-form
        :data="formData"
        :rules="rules"
        :label-width="100"
        :label-align="'top'"
        :reset-type="'initial'"
        @submit="onSubmit"
      >
        <t-form-item label="请选择联机类型" name="isHoster">
          <t-radio-group v-model="formData.isHoster">
            <t-radio :value="true">创建房间 - 房主</t-radio>
            <t-radio :value="false">加入房间 - 成员</t-radio>
          </t-radio-group>
        </t-form-item>
        <t-form-item label="房间号" name="roomId">
          <template #help>
            <span v-if="formData.isHoster">建议填写您的QQ号码。</span>
            <span v-else>请输入房间创建者提供的房间号。</span>
          </template>
          <t-input v-model="formData.roomId" placeholder="请输入房间号" />
        </t-form-item>
        <t-form-item label="房间密钥" name="roomKey">
          <template #help>
            <span v-if="formData.isHoster">随便写一个你喜欢的密钥。</span>
            <span v-else>请输入房间创建者提供的密钥。</span>
          </template>
          <t-input v-model="formData.roomKey" placeholder="请输入房间密钥" />
          <t-button v-if="formData.isHoster" theme="default" variant="base" style="margin-left: 5px;" @click="formData.roomKey = generateRandomString(16);">随机生成</t-button>
        </t-form-item>
        <t-form-item label="绑定端口" name="bindPort">
          <template #help>
            <span v-if="formData.isHoster">请输入游戏内提示的开放联机端口。</span>
            <span v-else>建议保持默认。</span>
          </template>
          <t-input v-model="formData.bindPort" placeholder="请输入绑定端口" />
        </t-form-item>

        <t-form-item style="margin-top: 16px;">
            <t-button theme="primary" type="submit">添加隧道</t-button>
        </t-form-item>

      </t-form>
    </t-card>
  </div>
</template>

<style scoped lang="less"></style>
