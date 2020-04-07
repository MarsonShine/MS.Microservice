using System;
using System.IO;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace MS.Microservice
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information) // 将Microsoft前缀的日志 最小输出级别改成 Information
                .Enrich.FromLogContext()
                .WriteTo.File(@"logs/log.txt", rollingInterval: RollingInterval.Day) // 将日志输出到目标路径，文件的生成方式为每天生成一个文件
                .WriteTo.Console()
                .CreateLogger();

            CreateWebHostBuilder(args).Run();
        }

        public static IWebHost CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseStartup<Startup>()
                .UseSerilog()
                .Build();
    }
}
