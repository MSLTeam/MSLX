import { defineStore } from 'pinia';
import type { NotificationItem } from '@/types/interface';

const msgData = [
  {
    id: '1',
    content: 'TvT',
    type: 'QwQ',
    status: true,
    collected: false,
    date: '2022-10-27 11:00',
    quality: 'high',
  },
];

type MsgDataType = typeof msgData;

export const useNotificationStore = defineStore('notification', {
  state: () => ({
    msgData,
  }),
  getters: {
    unreadMsg: (state) => state.msgData.filter((item: NotificationItem) => item.status),
    readMsg: (state) => state.msgData.filter((item: NotificationItem) => !item.status),
  },
  actions: {
    setMsgData(data: MsgDataType) {
      this.msgData = data;
    },
  },
  persist: true,
});
