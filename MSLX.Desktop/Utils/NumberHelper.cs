using System;
using System.Collections.Generic;
using System.Text;

namespace MSLX.Desktop.Utils
{
    internal class NumberHelper
    {
        /// <summary>
        /// 生成指定范围内的随机整数（包含起始和结束值）
        /// </summary>
        public static int GetRandomNumber(int start, int end)
        {
            if (start > end)
            {
                throw new ArgumentOutOfRangeException(nameof(start), "起始值不能大于结束值");
            }
            return (new Random()).Next(start, end + 1);
        }
    }
}
