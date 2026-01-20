using Avalonia;
using Avalonia.Controls.Primitives;
using MSLX.Desktop.Utils;
using System.Collections.ObjectModel;
using System.Linq;

namespace MSLX.Desktop.Controls;

public class DownloadDisplayControl : TemplatedControl
{
    public ObservableCollection<DownloadItem> Downloads { get; set; } = new ObservableCollection<DownloadItem>();

    public DownloadDisplayControl(){}

    public void AddTaskToUIDisplay(string itemId)
    {
        var item = DownloadManager.Instance.GetDownloadItem(itemId);
        if (item != null)
            Downloads.Add(item);
    }

    public void AddGroupToUIDisplay(string groupId)
    {
        var items = DownloadManager.Instance.GetGroupItems(groupId);
        if (items != null)
        {
            foreach (var item in items)
            {
                Downloads.Add(item);
            }
        }
    }

    public void RemoveTaskFromUIDisplay(string itemId)
    {
        var item = Downloads.FirstOrDefault(d => d.ItemId == itemId);
        if (item != null)
            Downloads.Remove(item);
    }

    public void RemoveGroupFromUIDisplay(string groupId)
    {
        var itemsToRemove = Downloads.Where(d => d.GroupId == groupId).ToList();
        foreach (var item in itemsToRemove)
        {
            Downloads.Remove(item);
        }
    }
}