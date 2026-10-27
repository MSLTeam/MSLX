const { defineComponent, h, ref, onMounted } = window.Vue;
const { Button, Input, Dialog } = window.TDesign;
const slots = [
  'instance-console-dropdown', 'instance-console-overview-bottom', 'instance-setting-more', 'instance-settings-tab',
  'dashboard-index-after-system-status', 'frp-console-control-panel-bottom', 'frp-create-provider',
  'settings-profile-bottom', 'settings-daemon-bottom'
];
function card(slot) {
  return defineComponent({
    props: ['serverId', 'instanceId', 'frpId', 'status', 'isRunning'],
    emits: ['saved', 'success'],
    setup(props, { expose, emit }) {
      const context = window.MSLX_NodeContext.use();
      const data = ref(''); const value = ref(''); const isOpen = ref(false);
      const load = async () => {
        try { const result = await context.request.get({ url: '/api/plugins/mslx-plugin-node-fixture/context' }); data.value = `node=${result.nodeId || 'local'} process=${result.process}`; }
        catch (error) { data.value = error.message; }
      };
      onMounted(load);
      expose({ open: () => { isOpen.value = true; }, close: () => { isOpen.value = false; }, isOpen });
      const content = () => h('div', { class: 'design-card relative flex flex-col gap-3 rounded-2xl border border-[var(--td-component-border)] shadow-sm p-5' }, [
        h('strong', slot), h('p', `context=${context.nodeId} resource=${props.serverId ?? props.frpId ?? '-'}`), h('p', data.value),
        h(Input, { value: value.value, placeholder: '验证配置', onChange: next => { value.value = next; } }),
        h(Button, { onClick: async () => {
          try { await context.request.post({ url: '/api/plugins/mslx-plugin-node-fixture/config', data: { fixtureValue: value.value } }); data.value = '已保存到 ' + context.nodeId; emit('saved'); emit('success'); }
          catch (error) { data.value = error.message; }
        } }, { default: () => '保存验证配置' }),
      ]);
      return () => slot === 'instance-console-dropdown' ? h(Dialog, { visible: isOpen.value, header: '插件节点验证', attach: 'body', width: 'min(580px,92vw)', onClose: () => { isOpen.value = false; }, onConfirm: () => { isOpen.value = false; } }, { default: content }) : content();
    }
  });
}
export const pluginConfig = {
  name: 'mslx-plugin-node-fixture', version: '1.0.0',
  routes: [{ path: '/mslx-plugin-node-fixture', name: 'NodeFixturePage', component: 'HOST_LAYOUT', meta: { title: '多节点验证插件', icon: 'extension', roleCode: ['admin', 'user'] }, children: [{ path: '', name: 'NodeFixtureHome', component: card('standalone'), meta: { hidden: true } }] }],
  extensions: slots.map(slot => ({ slot, component: card(slot), label: '多节点验证插件' }))
};
