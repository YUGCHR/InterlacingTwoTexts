using System;
using System.Collections.Generic;
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
    public interface IEventCounterHandler
    {
        public void EventCounterInit(ConstantsSet constantsSet);
        public Task EventCounterOccurred(ConstantsSet constantsSet, string eventKey, CancellationToken stoppingToken);
        public void Dispose();
    }

    public class EventCounterHandler : IEventCounterHandler
    {
        private readonly CancellationToken _cancellationToken;       
        private readonly ICacheManageService _cache;        
        private readonly IIntegrationTestService _test;
        private readonly ITaskPackageFormationFromPlainText _front;

        public EventCounterHandler(
            IHostApplicationLifetime applicationLifetime,            
            ICacheManageService cache,            
            IIntegrationTestService test,
            ITaskPackageFormationFromPlainText front
            )
        {
            _cancellationToken = applicationLifetime.ApplicationStopping;           
            _cache = cache;           
            _test = test;
            _front = front;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<EventCounterHandler>();

        private Timer _timer;
        private bool _timerCanBeStarted;
        private bool _handlerCallingsMergeCanBeCalled;
        private int _callingNumOfEventFrom;
        private bool _isTestInProgress;
        
        public void EventCounterInit(ConstantsSet constantsSet)
        {            
            _callingNumOfEventFrom = 0;
            _handlerCallingsMergeCanBeCalled = true;

            // инициализация таймера (DoWork не сработает с нулевым счетчиком)
            _timer = new Timer(DoWork, constantsSet, 0, Timeout.Infinite);
            _timerCanBeStarted = true;
        }

        // bool isTestNotInProgress = !_isTestInProgress;
        
        public Task EventCounterOccurred(ConstantsSet constantsSet, string eventKey, CancellationToken stoppingToken)
        {
            // считать вызовы подписки и запустить таймер после первого (второго?) вызова
            int count = Interlocked.Increment(ref _callingNumOfEventFrom);
            Logs.Here().Information("Key {0} was received for the {1} time, count = {2}.\n", eventKey, _callingNumOfEventFrom, count);

            // на втором вызове запускаем таймер на N секунд (второй вызов - это 2, а не 1)

            int countTrackingStart = constantsSet.IntegerConstant.BackgroundDispatcherConstant.CountTrackingStart.Value; // 2
            int countDecisionMaking = constantsSet.IntegerConstant.BackgroundDispatcherConstant.CountDecisionMaking.Value; // 6

            //count = Volatile.Read(ref _callingNumOfEventFrom);

            if (_timerCanBeStarted && count > countTrackingStart - 1)
            {
                Logs.Here().Information("Event count {0} == {1} was discovered.", count, countTrackingStart);
                _ = StartTimerOnce(constantsSet, _cancellationToken);
            }

            if (count > countDecisionMaking - 1)
            {
                Logs.Here().Information("Event count {0} == {1} was discovered.", count, countDecisionMaking);

                // сразу же сбросить счётчик событий                
                count = Interlocked.Exchange(ref _callingNumOfEventFrom, 0);
                Logs.Here().Information("_callingNumOfEventFrom {0} was reset and count = {1}.", _callingNumOfEventFrom, count);

                _ = HandlerMergeOfCalling(constantsSet);

                Logs.Here().Information("EventCounter was elapsed.");
            }

            return Task.CompletedTask;
        }

        // обработчик слияния вызовов по счётчику и таймеру - может, CounterAndTimerCallMergeHandler - ?
        private async Task HandlerMergeOfCalling(ConstantsSet constantsSet)
        {
            // слияние вызовов обработчика из таймера и из счётчика
            Logs.Here().Information("HandlerCallingsMerge was started.");

            // остановить и сбросить таймер (не очищать?)
            Logs.Here().Information("Timer will be stopped.");
            _ = StopTimer(_cancellationToken);

            // предусмотреть блокировку повторного вызова метода слияния (не повторного, а сдвоенного - от счетчика и таймера одновременно)
            while (!_handlerCallingsMergeCanBeCalled)
            {
                Logs.Here().Warning("   ********** HandlerCallingsMerge double call was detected! ********** ");
                await Task.Delay(100);
            }

            _handlerCallingsMergeCanBeCalled = false;

            // вот здесь подходящий момент, когда не надо спешить и можно определить, что сейчас - тест или работа
            // если будет следующий вызов, то он повисит в ожидании
            // вот только его поля уже будет доступны...
            // но если тест, то поле все равно будет только одно - оно перезапишется
            // а если будет хоть одно рабочее поле, то тест отменится, даже если он был запущен раньше
            // всё поменялось, тест при запуске ставит флаг _isTestInProgress в true и остальные разбегаются в стороны

            // тут можно возвращать true из обработчика - с await, это будет означать, что он освободился и готов принять событие во второй поток
            _handlerCallingsMergeCanBeCalled = await _front.HandlerCallingDistributore(constantsSet, _isTestInProgress, _cancellationToken);
            Logs.Here().Information("HandlerCallingDistributore returned calling unblock. {@F}", new { Flag = _handlerCallingsMergeCanBeCalled });
        }        

        private Task StartTimerOnce(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            if (_timerCanBeStarted)
            {
                _timerCanBeStarted = false;
                int timerIntervalInMilliseconds = constantsSet.TimerIntervalInMilliseconds.Value;
                Logs.Here().Information("Timer will be started for {0} msec.", timerIntervalInMilliseconds);

                // таймер с однократной сработкой через интервал timerIntervalInMilliseconds
                _timer?.Change(timerIntervalInMilliseconds, Timeout.Infinite);
                //_timer = new Timer(DoWork, constantsSet, timerIntervalInMilliseconds, Timeout.Infinite);

                Logs.Here().Information("Timer was started for {0} msec, _isTimerStarted = {1}.", timerIntervalInMilliseconds, _timerCanBeStarted);
            }
            return Task.CompletedTask;
        }

        private void DoWork(object state)
        {
            // сюда попали, когда вышло время ожидания по таймеру

            ConstantsSet constantsSet = (ConstantsSet)state;
            int countTrackingStart = constantsSet.IntegerConstant.BackgroundDispatcherConstant.CountTrackingStart.Value; // 2
            var count = Volatile.Read(ref _callingNumOfEventFrom);

            // проверка для пропуска инициализации таймера
            if (count < countTrackingStart)
            {
                Logs.Here().Information("_timer = new Timer(DoWork, constantsSet, 0, Timeout.Infinite). {0} < {1}", count, countTrackingStart);
                return;
            }

            Logs.Here().Information("_callingNumOfEventFrom {0} was Volatile.Read and count = {1}.", _callingNumOfEventFrom, count);
            Logs.Here().Information("Timer called DoWork.");

            // сразу же сбросить счётчик событий
            count = Interlocked.Exchange(ref _callingNumOfEventFrom, 0);
            Logs.Here().Information("_callingNumOfEventFrom {0} was reset and count = {1}.", _callingNumOfEventFrom, count);

            _ = HandlerMergeOfCalling(constantsSet);
            Logs.Here().Information("HandlerCallingsMerge was called.");
        }

        private Task StopTimer(CancellationToken stoppingToken)
        {
            _timerCanBeStarted = true;

            Logs.Here().Information("Timer is stopping.");

            _timer?.Change(Timeout.Infinite, 0);

            Logs.Here().Information("Timer state _isTimerStarted = {0}.", _timerCanBeStarted);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
