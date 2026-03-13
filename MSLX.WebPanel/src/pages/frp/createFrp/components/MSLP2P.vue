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
    <div class="design-card bg-[var(--td-bg-color-container)]/80 backdrop-blur-md rounded-2xl border border-[var(--td-component-border)] shadow-sm p-6 sm:p-8">

      <div class="mb-6 pb-4 border-b border-dashed border-zinc-200/70 dark:border-zinc-700/60">
        <h3 class="text-lg font-bold text-[var(--td-text-color-primary)] m-0 leading-none">MSL P2P 联机隧道</h3>
      </div>

      <t-form
        :data="formData"
        :rules="rules"
        label-align="top"
        reset-type="initial"
        @submit="onSubmit"
      >
        <t-form-item label="请选择联机类型" name="isHoster">
          <t-radio-group v-model="formData.isHoster" variant="default-filled">
            <t-radio-button :value="true">创建房间 - 房主</t-radio-button>
            <t-radio-button :value="false">加入房间 - 成员</t-radio-button>
          </t-radio-group>
        </t-form-item>

        <t-form-item label="房间号" name="roomId">
          <template #help>
            <span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block">
              <span v-if="formData.isHoster">建议填写您的 QQ 号码。</span>
              <span v-else>请输入房间创建者提供的房间号。</span>
            </span>
          </template>
          <t-input v-model="formData.roomId" placeholder="请输入房间号" class="!w-full sm:!w-96" />
        </t-form-item>

        <t-form-item label="房间密钥" name="roomKey">
          <template #help>
            <span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block">
              <span v-if="formData.isHoster">随便写一个你喜欢的密钥。</span>
              <span v-else>请输入房间创建者提供的密钥。</span>
            </span>
          </template>
          <div class="flex items-center gap-3 w-full sm:w-[28rem]">
            <t-input v-model="formData.roomKey" placeholder="请输入房间密钥" class="!flex-1" />
            <transition name="fade">
              <t-button
                v-if="formData.isHoster"
                theme="default"
                variant="base"
                class="shrink-0 !bg-zinc-100 dark:!bg-zinc-800/80 !border-none !text-zinc-700 dark:!text-zinc-300 hover:!bg-zinc-200 dark:hover:!bg-zinc-700 !rounded-lg"
                @click="formData.roomKey = generateRandomString(16);"
              >
                随机生成
              </t-button>
            </transition>
          </div>
        </t-form-item>

        <t-form-item label="绑定端口" name="bindPort">
          <template #help>
            <span class="text-[11px] font-medium text-[var(--td-text-color-secondary)] mt-1 inline-block">
              <span v-if="formData.isHoster">请输入游戏内提示的开放联机端口。</span>
              <span v-else>建议保持默认。</span>
            </span>
          </template>
          <t-input v-model="formData.bindPort" placeholder="请输入绑定端口" class="!w-full sm:!w-96" />
        </t-form-item>

        <div class="mt-8 pt-5 border-t border-dashed border-zinc-200/70 dark:border-zinc-700/60">
          <t-button theme="primary" type="submit" class="!rounded-xl !font-bold !px-8 shadow-md shadow-[var(--color-primary-light)]/30 hover:shadow-[var(--color-primary-light)]/50">添加隧道</t-button>
        </div>

      </t-form>
    </div>
  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";

/* 按钮的淡入淡出过渡动画 */
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.3s ease, transform 0.3s ease;
}
.fade-enter-from,
.fade-leave-to {
  opacity: 0;
  transform: translateX(-10px);
}
</style>
