using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MS.Microservice.Core.Timer
{
    public interface ITimer
    {
        /// <summary>
        /// 计算程序运行结束的当前时间，单位为纳秒
        /// </summary>
        /// <returns></returns>
        long CurrentTime();
        /// <summary>
        /// 结束计时
        /// </summary>
        /// <returns>返回当前的时间，纳秒</returns>
        long EndRecording();
        /// <summary>
        /// 开始计时
        /// </summary>
        /// <returns>返回当前的时间，纳秒</returns>
        long StartRecording();
        /// <summary>
        /// 重置
        /// </summary>
        void Reset();
    }
}
