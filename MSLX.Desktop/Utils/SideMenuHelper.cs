using Avalonia.Controls;
using MSLX.Desktop.Models;
using SukiUI.Controls;
using System.Linq;

namespace MSLX.Desktop.Utils
{
    public class SideMenuHelper
    {
        public SukiSideMenu SideMenu { get; set; } = new SukiSideMenu();
        public static SideMenuHelper? MainSideMenuHelper { get; set; }

        public void NavigateTo<T>() where T : UserControl
        {
            var page = PageStore.MainPages.FirstOrDefault(p => p.PageContent is T);
            if (page != null)
                SideMenu.SelectedItem = page;
        }

        public void NavigateTo(SukiSideMenuItem sukiSideMenuItem, bool addToSideMenu = false, int insert = -1)
        {
            if (addToSideMenu)
            {
                if (insert != -1)
                    PageStore.MainPages.Insert(insert, sukiSideMenuItem);
                else
                    PageStore.MainPages.Add(sukiSideMenuItem);
            }
            SideMenu.SelectedItem = sukiSideMenuItem;
        }

        public void NavigateToIndex(int index)
        {
            if (index >= 0 && index < PageStore.MainPages.Count)
            {
                SideMenu.SelectedItem = PageStore.MainPages[index];
            }
        }

        public void NavigateRemove<T>() where T : UserControl
        {
            var page = PageStore.MainPages.FirstOrDefault(p => p.PageContent is T);
            if (page != null)
            {
                if (SideMenu.SelectedItem == page)
                    SideMenu.SelectedItem = PageStore.MainPages.FirstOrDefault() ?? new SukiSideMenuItem();
                PageStore.MainPages.Remove(page);
            }
        }

        public void NavigateRemove(UserControl userControl)
        {
            var page = PageStore.MainPages.FirstOrDefault(p => p.PageContent.GetType() == userControl.GetType());
            if (page != null)
            {
                if (SideMenu.SelectedItem == page)
                    SideMenu.SelectedItem = PageStore.MainPages.FirstOrDefault() ?? new SukiSideMenuItem();
                PageStore.MainPages.Remove(page);
            }
        }

        public int GetActivePageIndex()
        {
            var page = PageStore.MainPages.FirstOrDefault(p => p == SideMenu.SelectedItem);
            if (page != null)
                return PageStore.MainPages.IndexOf(page);
            return -1;
        }

        public void HideMainPages(int skip = -1)
        {
            if (skip != -1)
            {
                for (int i = 0; i < PageStore.MainPages.Count; i++)
                {
                    if (i == skip)
                        continue;
                    PageStore.MainPages[i].IsVisible = false;
                }
                return;
            }
            foreach (var page in PageStore.MainPages)
            {
                page.IsVisible = false;
            }
        }

        public void ShowMainPages(int skip = -1)
        {
            if (skip != -1)
            {
                for (int i = 0; i < PageStore.MainPages.Count; i++)
                {
                    if (i == skip)
                        continue;
                    PageStore.MainPages[i].IsVisible = true;
                }
                return;
            }
            foreach (var page in PageStore.MainPages)
            {
                page.IsVisible = true;
            }
        }
    }
}
