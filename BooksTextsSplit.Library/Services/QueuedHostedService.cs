using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Library.Models;
using Shared.Library.Services;

namespace BooksTextsSplit.Library.Services
{
    public class QueuedHostedService : BackgroundService
    {
        private readonly ISettingConstantsS _constants;
        private readonly ILogger<QueuedHostedService> _logger;

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<QueuedHostedService>();

        public QueuedHostedService(
            IBackgroundTaskQueue taskQueue,
            ISettingConstantsS constants,
            ILogger<QueuedHostedService> logger)
        {
            TaskQueue = taskQueue;
            _constants = constants;
            _logger = logger;
        }

        public IBackgroundTaskQueue TaskQueue { get; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logs.Here().Information("Queued Hosted Service was started.\n");

            ConstantsSet constantsSet = await _constants.ConstantInitializer(stoppingToken);

            _logger.LogInformation(
                $"Queued Hosted Service is running with concurrent 3 Tasks.{Environment.NewLine}" +
                $"{Environment.NewLine}Send get Worker to add a work item to the " +
                $"background queue.{Environment.NewLine}");

            // вызвать метод подготовки констант из ControllerDataManager

            await BackgroundProcessing(stoppingToken);
        }

        private async Task BackgroundProcessing(CancellationToken stoppingToken)
        {
            //Console.WriteLine("Start");
            Action<CancellationToken> process = async token =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    //Console.WriteLine("Loop");
                    var workItem = await TaskQueue.DequeueAsync(stoppingToken);
                    //Console.WriteLine("Dequeued a task");
                    try
                    {
                        //Console.WriteLine("Start await task");
                        await workItem(stoppingToken);
                        //Console.WriteLine("Finished a work item");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error occurred executing {nameof(workItem)}");
                    }
                }
            };

            var task1 = Task.Run(() => process(stoppingToken), stoppingToken);
            var task2 = Task.Run(() => process(stoppingToken), stoppingToken);
            var task3 = Task.Run(() => process(stoppingToken), stoppingToken);

            await Task.WhenAll(task1, task2, task3);
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Queued Hosted Service is stopping.");

            await base.StopAsync(stoppingToken);
        }
    }
}
