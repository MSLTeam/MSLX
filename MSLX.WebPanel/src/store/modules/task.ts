import { defineStore } from 'pinia';
import { ref, computed, watch } from 'vue';
import { getUserTasks, cancelUserTask, deleteUserTask, clearFinishedUserTasks } from '@/api/files';
import { MessagePlugin } from 'tdesign-vue-next';
import { useNodeStore } from './node';

export const useTaskStore = defineStore('backgroundTask', () => {
  const tasks = ref<any[]>([]);
  let pollTimer: number | null = null;
  const isPolling = ref(false);
  const nodeStore = useNodeStore();

  const runningCount = computed(() => {
    return tasks.value.filter(t => t.state === 0 || t.state === 1).length; // 0: Pending, 1: Running
  });

  watch(() => nodeStore.activeNodeId, () => {
    tasks.value = [];
    stopPolling();
    fetchTasks();
  });

  const fetchTasks = async (instanceId?: number) => {
    try {
      const res = await getUserTasks(instanceId);
      const oldTasks = tasks.value;
      tasks.value = res || [];

      // 状态查询
      if (oldTasks.length > 0) {
        tasks.value.forEach(newTask => {
          const oldTask = oldTasks.find(t => t.id === newTask.id);
          if (oldTask && (oldTask.state === 0 || oldTask.state === 1)) {
            if (newTask.state === 2) { // Success
              MessagePlugin.success(`任务 [${newTask.title}] 已完成`);
            } else if (newTask.state === 3) { // Failed
              MessagePlugin.error(`任务 [${newTask.title}] 失败: ${newTask.message}`);
            }
          }
        });
      }

      managePolling();
    } catch (error) {
      console.error('Failed to fetch tasks', error);
    }
  };

  const managePolling = () => {
    if (runningCount.value > 0 && !isPolling.value) {
      startPolling();
    } else if (runningCount.value === 0 && isPolling.value) {
      stopPolling();
    }
  };

  const startPolling = () => {
    if (pollTimer) return;
    isPolling.value = true;
    pollTimer = window.setInterval(() => {
      fetchTasks();
    }, 2000);
  };

  const stopPolling = () => {
    if (pollTimer) {
      clearInterval(pollTimer);
      pollTimer = null;
    }
    isPolling.value = false;
  };

  const cancelTask = async (taskId: string) => {
    await cancelUserTask(taskId);
    await fetchTasks();
  };

  const deleteTask = async (taskId: string) => {
    await deleteUserTask(taskId);
    await fetchTasks();
  };

  const clearFinished = async () => {
    await clearFinishedUserTasks();
    await fetchTasks();
  };

  return {
    tasks,
    runningCount,
    fetchTasks,
    startPolling,
    stopPolling,
    cancelTask,
    deleteTask,
    clearFinished
  };
});
