using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Collections.Generic;
using System.Text;

namespace MSLX.Desktop.Utils
{
    internal class StringHelper
    {
        /// <summary>
        /// 生成指定长度的随机字符串（可选前缀）
        /// </summary>
        /// <param name="length">随机字符串长度</param>
        /// <param name="prefix">可选前缀（默认无）</param>
        public static string GenerateRandomString(int length, string? prefix = null)
        {
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "长度不能为负数");
            }

            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var randomChars = new char[length];
            for (int i = 0; i < length; i++)
            {
                randomChars[i] = chars[(new Random()).Next(chars.Length)];
            }

            return (prefix ?? "") + new string(randomChars);
        }

        public static async void CopyToClipboard(string text)
        {
            var appLifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (appLifetime != null)
            {
                var mainWindow = appLifetime.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    var clipboard = mainWindow.Clipboard;
                    if (clipboard != null)
                        await clipboard.SetTextAsync(text);
                }
            }
        }
    }
}
