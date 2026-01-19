using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Collections.Generic;
using System.Text;

namespace MSLX.Desktop.Utils
{
    internal class DialogService
    {
        public static ISukiDialogManager DialogManager { get; set; } = new SukiDialogManager();
        public static ISukiToastManager ToastManager { get; set; } = new SukiToastManager();
    }
}
