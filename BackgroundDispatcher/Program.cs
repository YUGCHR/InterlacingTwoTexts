using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using CachingFramework.Redis;
using CachingFramework.Redis.Contracts.Providers;
using StackExchange.Redis;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using Shared.Library.Services;
using BackgroundDispatcher.Services;

namespace BackgroundDispatcher
{
    public class Program
    {
        private static Serilog.ILogger Logs => Serilog.Log.ForContext<Program>();

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .UseContentRoot(Directory.GetCurrentDirectory())
            .ConfigureAppConfiguration((hostContext, config) =>
            {
                var env = hostContext.HostingEnvironment;

                // find the shared folder in the parent folder
                //string[] paths = { env.ContentRootPath, "..", "SharedSettings" };
                //var sharedFolder = Path.Combine(paths);

                //load the SharedSettings first, so that appsettings.json overrwrites it
                config
                    //.AddJsonFile(Path.Combine(sharedFolder, "sharedSettings.json"), optional: true)
                    .AddJsonFile("appsettings.json", optional: true)
                    .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);

                config.AddEnvironmentVariables();
            })
            .ConfigureLogging((ctx, sLog) =>
            {
                //var seriLog = new LoggerConfiguration()
                //    .WriteTo.Console()
                //    .CreateLogger();

                //var outputTemplate = "{Timestamp:HH:mm} [{Level:u3}] ({ThreadId}) {Message}{NewLine}{Exception}";
                //var outputTemplate = "[{Timestamp:HH:mm:ss} {Level}] {SourceContext}{NewLine}{Message}{NewLine}in method {MemberName} at {FilePath}:{LineNumber}{NewLine}{Exception}{NewLine}";
                string outputTemplate = "{NewLine}[{Timestamp:HH:mm:ss} {Level:u3} ({ThreadId}) {SourceContext}.{MemberName} - {LineNumber}] {NewLine} {Message} {NewLine} {Exception}";

                //seriLog.Information("Hello, Serilog!");

                //Log.Logger = seriLog;
                // попробовать менять уровень вывода логгера через переменную
                // const LogEventLevel loggerLevel = LogEventLevel.Debug;
                // https://stackoverflow.com/questions/25477415/how-can-i-reconfigure-serilog-without-restarting-the-application
                // https://stackoverflow.com/questions/51389550/serilog-json-config-logginglevelswitch-access
                const LogEventLevel loggerLevel = LogEventLevel.Information;
                Log.Logger = new LoggerConfiguration()
                    .Enrich.With(new ThreadIdEnricher())
                    .Enrich.FromLogContext()
                    .MinimumLevel.Verbose()
                    .WriteTo.Console(restrictedToMinimumLevel: loggerLevel, outputTemplate: outputTemplate, theme: AnsiConsoleTheme.Literate) //.Verbose .Debug .Information .Warning .Error .Fatal
                    .WriteTo.File("logs/BackgroundTasksQueue{Date}.txt", rollingInterval: RollingInterval.Day, outputTemplate: outputTemplate)
                    .CreateLogger();

                Logs.Here().Information("The global logger Serilog has been configured.\n");
            })
            .UseDefaultServiceProvider((ctx, opts) => { /* elided for brevity */ })
            .ConfigureServices((hostContext, services) =>
                {
                    try
                    {
                        //ConnectionMultiplexer muxer = ConnectionMultiplexer.Connect("redis");
                        ConnectionMultiplexer muxer = ConnectionMultiplexer.Connect("localhost");
                        services.AddSingleton<ICacheProviderAsync>(new RedisContext(muxer).Cache);
                        services.AddSingleton(new RedisContext(muxer).KeyEvents);
                    }
                    catch (Exception ex)
                    {
                        string message = ex.Message;
                        Console.WriteLine($"\n\n Redis server did not start: \n + {message} \n");
                        throw;
                    }

                    services.AddSingleton<ISettingConstantsS, SettingConstantsService>(); // new one
                    services.AddScoped<MonitorLoop>();
                    services.AddScoped<GenerateThisInstanceGuidService>();
                    services.AddScoped<ICacheManageService, CacheManageService>();
                    services.AddScoped<ISharedDataAccess, SharedDataAccess>();                    
                    services.AddScoped<IOnKeysEventsSubscribeService, OnKeysEventsSubscribeService>();
                    services.AddScoped<ITaskPackageFormationFromPlainText, TaskPackageFormationFromPlainText>();

                });

        public static void Main(string[] args)
        {
            IHost host = CreateHostBuilder(args).Build();

            MonitorLoop monitorLoop = host.Services.GetRequiredService<MonitorLoop>();
            monitorLoop.StartMonitorLoop();

            host.Run();
        }
    }

    internal class ThreadIdEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                "ThreadId", Thread.CurrentThread.ManagedThreadId));
        }
    }

    public static class LoggerExtensions
    {
        // https://stackoverflow.com/questions/29470863/serilog-output-enrich-all-messages-with-methodname-from-which-log-entry-was-ca/46905798

        public static ILogger Here(this ILogger logger, [CallerMemberName] string memberName = "", [CallerLineNumber] int sourceLineNumber = 0)
        //[CallerFilePath] string sourceFilePath = "",
        {
            return logger.ForContext("MemberName", memberName).ForContext("LineNumber", sourceLineNumber);
            //.ForContext("FilePath", sourceFilePath)
        }
    }


    // вставить генерацию уникального номера в сервис констант - уже нет, оставить здесь
    // может и сервис генерации уникального номера сделать отдельным sln/container, к которому все будут обращаться
    // скажем, этот сервис будет подписан на стандартный для всех ключ запрос
    // по срабатыванию подписки на этот ключ, метод будет просто считать количество обращений - чтобы никого не пропустить
    // потом - с задержкой, когда счётчик будет стоять больше определенного времени, он создаст стандартный ключ с полями - гуид
    // и все запросившие гуид будут их разбирать таким же способом, как и пакеты задач

    //public class GenerateThisBackServerGuid
    //{
    //    private readonly string _thisBackServerGuid;

    //    public GenerateThisBackServerGuid()
    //    {
    //        _thisBackServerGuid = Guid.NewGuid().ToString();
    //    }

    //    public string ThisBackServerGuid()
    //    {
    //        return _thisBackServerGuid;
    //    }
    //}
}

// appsettings sharing between many solutions
//var settingPath = Path.GetFullPath(Path.Combine(@"../../appsettings.json")); // get absolute path
//var builder = new ConfigurationBuilder()
//        .SetBasePath(env.ContentRootPath)
//        .AddJsonFile(settingPath, optional: false, reloadOnChange: true);