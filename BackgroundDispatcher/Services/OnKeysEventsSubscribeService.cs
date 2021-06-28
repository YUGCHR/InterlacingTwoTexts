using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CachingFramework.Redis.Contracts;
using CachingFramework.Redis.Contracts.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Library.Models;
using Shared.Library.Services;

namespace BackgroundDispatcher.Services
{
    public interface IOnKeysEventsSubscribeService
    {
        public void SubscribeOnEventFrom(ConstantsSet constantsSet);
    }

    public class OnKeysEventsSubscribeService : IOnKeysEventsSubscribeService
    {

        private readonly CancellationToken _cancellationToken;
        private readonly ILogger<OnKeysEventsSubscribeService> _logger;
        private readonly ICacheManageService _cache;
        private readonly IKeyEventsProvider _keyEvents;
        private readonly ITaskPackageFormationFromPlainText _front;

        public OnKeysEventsSubscribeService(
            IHostApplicationLifetime applicationLifetime,
            ILogger<OnKeysEventsSubscribeService> logger,
            ICacheManageService cache,
            IKeyEventsProvider keyEvents,
            ITaskPackageFormationFromPlainText front
            )
        {
            _cancellationToken = applicationLifetime.ApplicationStopping;
            _logger = logger;
            _cache = cache;
            _keyEvents = keyEvents;
            _front = front;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<OnKeysEventsSubscribeService>();
        private Timer _timer;
        private bool _flagToBlockDoubleCall;
        private int _callingNumOfEventFrom;

        public void SubscribeOnEventFrom(ConstantsSet constantsSet)
        {
            // здесь сделать такой же механизм с блокировкой и проверкой пропущенных, как у бэк-сервера
            // нет, здесь свой механизм - с накоплением и ожиданием по таймеру

            string eventKey = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
            KeyEvent eventCmd = constantsSet.EventCmd; // HashSet
            _callingNumOfEventFrom = 0;
            _flagToBlockDoubleCall = true;

            //Stopwatch stopWatch = new Stopwatch();
            //stopWatch.Start();
            // а в диспетчере складывать поступившие ключи в лист, а потом его обрабатывать, обработанные - в другой лист?
            // нужен ли конкурентный доступ?
            // диспетчер может ждать 10 секунд или 10 книг (что раньше, из констант) и брать это количество из листа
            // отмечать как обработанные, формировать пакет и отдавать бэк-серверу
            // не лист, а ключ!
            // держать переменную класса с текущим значением счётчика задач и while по истечению времени или нужного приращения этого счётчика

            _keyEvents.Subscribe(eventKey, (key, cmd) => // async
            {
                if (cmd == eventCmd)
                {
                    // считать вызовы подписки и запустить таймер после первого (второго?) вызова
                    int count = Interlocked.Increment(ref _callingNumOfEventFrom);
                    //count = Volatile.Read(ref _callingNumOfEventFrom);
                    Logs.Here().Information("Key {0} was received for the {1} time, count {2} == 2 will be cheked.\n", eventKey, _callingNumOfEventFrom, count);

                    // на втором вызове запускаем таймер на N секунд (второй вызов - это 2, а не 1)
                    
                    if (count == 2)
                    {
                        Logs.Here().Information("Subscribed keyEvents count = {0}. StartTimerOnce will be called.", count);

                        _ = StartTimerOnce(constantsSet, _cancellationToken);                        
                    }

                    //stopWatch.Stop();
                    //TimeSpan ts = stopWatch.Elapsed; // Get the elapsed time as a TimeSpan value.
                    Logs.Here().Information("count {0} == 6 will be cheked.", count);


                    if (count == 6)
                    {
                        // сразу же сбросить счётчик событий
                        // to reset
                        count = Interlocked.Exchange(ref _callingNumOfEventFrom, 0);
                        Logs.Here().Information("_callingNumOfEventFrom {0} was reset and count = {1}.", _callingNumOfEventFrom, count);

                        // сразу же сбросить таймер
                        // остановить и сбросить таймер (не очищать?)
                        Logs.Here().Information("Timer will be stopped.");
                        _ = StopTimer(_cancellationToken);

                        // здесь таймер уже мог запустить метод слияния
                        // поэтому надо предусмотреть там блокировку двойного вызова

                        _ = HandlerCallingsMerge(constantsSet);
                        Logs.Here().Information("HandlerCallingsMerge was called.");

                        //_logger.LogInformation("Tasks Packages created in count = {0}.", tasksPackagesCount);
                    }
                }
            });

            string eventKeyCommand = $"Key = {eventKey}, Command = {eventCmd}";
            _logger.LogInformation("You subscribed on event - {EventKey}.", eventKeyCommand);
            _logger.LogInformation("To start the front emulation please send from Redis console the following command - \n{_}{0} {1} count NN (NN - packages count).", "      ", eventCmd, eventKey);
        }

        private Task HandlerCallingsMerge(ConstantsSet constantsSet)
        {
            // слияние вызовов обработчика из таймера и из счётчика
            Logs.Here().Information("HandlerCallingsMerge was started.");

            // предусмотреть блокировку повторного вызова метода слияния
            if (_flagToBlockDoubleCall)
            {
                _flagToBlockDoubleCall = false;

                _ = _front.FrontServerEmulationMain(constantsSet, _cancellationToken);
                Logs.Here().Information("FrontServerEmulationMain was called.");
            }
            else
            {
                Logs.Here().Warning("   ********** HandlerCallingsMerge double call was detected! ********** ");
            }

            return Task.CompletedTask;
        }

        public Task StartTimerOnce(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            //_logger.LogInformation("Timed Hosted Service running.");
            //double nSec = 15;
            int timerIntervalInMilliseconds = constantsSet.TimerIntervalInMilliseconds.Value;
            Logs.Here().Information("Timer will be started for {0} msec.", timerIntervalInMilliseconds);

            // как остановить таймер по stoppingToken?
            //_timer = new Timer(DoWork, constantsSet, TimeSpan.Zero, TimeSpan.FromSeconds(nSec));
            // таймер с однократной сработкой через интервал timerIntervalInMilliseconds
            _timer = new Timer(DoWork, constantsSet, timerIntervalInMilliseconds, Timeout.Infinite);

            Logs.Here().Information("Timer was started for {0} msec.", timerIntervalInMilliseconds);

            return Task.CompletedTask;
        }

        private void DoWork(object state)
        {
            // сюда попали, когда вышло время ожидания по таймеру
            // если в это время закончится счётчик, то ничего делать уже не надо - пойдёт вызов по счётчику
            // поэтому перед вызовом обработчика ещё раз проверить счётчик
            // хотя это и не гарантирует отсутствия двойного вызова
            // но ничего, в обработчике закроем стандартные двери за первым вошедшим

            Logs.Here().Information("Timer called DoWork.");

            ConstantsSet constantsSet = (ConstantsSet)state;

            var count = Volatile.Read(ref _callingNumOfEventFrom);
            Logs.Here().Information("_callingNumOfEventFrom {0} was Volatile.Read and count = {1}.", _callingNumOfEventFrom, count);

            // сразу же сбросить счётчик событий
            // to reset
            count = Interlocked.Exchange(ref _callingNumOfEventFrom, 0);
            Logs.Here().Information("_callingNumOfEventFrom {0} was reset and count = {1}.", _callingNumOfEventFrom, count);

            _ = HandlerCallingsMerge(constantsSet);
            Logs.Here().Information("HandlerCallingsMerge was called.");

            //_logger.LogInformation("Timed Hosted Service is working. Count: {Count}", count);


        }

        public Task StopTimer(CancellationToken stoppingToken)
        {
            Logs.Here().Information("Timer is stopping.");

            _timer?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
