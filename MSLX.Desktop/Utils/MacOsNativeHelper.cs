using System;
using System.Runtime.InteropServices;

namespace MSLX.Desktop.Utils
{
    internal static class MacOsNativeHelper
    {
        private const string ObjCLib = "/usr/lib/libobjc.dylib";

        [DllImport(ObjCLib)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport(ObjCLib)]
        private static extern IntPtr sel_registerName(string name);

        [DllImport(ObjCLib)]
        private static extern IntPtr objc_msgSend(IntPtr self, IntPtr op);

        [DllImport(ObjCLib)]
        private static extern IntPtr objc_msgSend(IntPtr self, IntPtr op, IntPtr arg);

        public static void HideApp()
        {
            if (!OperatingSystem.IsMacOS()) return;
            try
            {
                var nsAppCls = objc_getClass("NSApplication");
                var nsApp = objc_msgSend(nsAppCls, sel_registerName("sharedApplication"));
                objc_msgSend(nsApp, sel_registerName("hide:"), IntPtr.Zero);
            }
            catch { }
        }

        public static void HideOthers()
        {
            if (!OperatingSystem.IsMacOS()) return;
            try
            {
                var nsAppCls = objc_getClass("NSApplication");
                var nsApp = objc_msgSend(nsAppCls, sel_registerName("sharedApplication"));
                objc_msgSend(nsApp, sel_registerName("hideOtherApplications:"), IntPtr.Zero);
            }
            catch { }
        }

        public static void ShowAll()
        {
            if (!OperatingSystem.IsMacOS()) return;
            try
            {
                var nsAppCls = objc_getClass("NSApplication");
                var nsApp = objc_msgSend(nsAppCls, sel_registerName("sharedApplication"));
                objc_msgSend(nsApp, sel_registerName("unhideAllApplications:"), IntPtr.Zero);
            }
            catch { }
        }
    }
}
