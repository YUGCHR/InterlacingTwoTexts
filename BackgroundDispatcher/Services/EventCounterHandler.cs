using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CachingFramework.Redis.Contracts;
using CachingFramework.Redis.Contracts.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Library.Models;
using Shared.Library.Services;

// план работ -

namespace BackgroundDispatcher.Services
{
    public interface IEventCounterHandler
    {
        public void EventCounterInit(ConstantsSet constantsSet);
        public Task<bool> IsCounterZeroReading(ConstantsSet constantsSet);
        public Task EventCounterOccurred(ConstantsSet constantsSet, string eventKey, int currentChainSerialNum, CancellationToken stoppingToken);
        public void Dispose();
    }

    public class EventCounterHandler : IEventCounterHandler
    {
        private readonly CancellationToken _cancellationToken;
        private readonly ICacheManagerService _cache; // TO REMOVE
        private readonly ITestOfComplexIntegrityMainServicee _test;
        private readonly ITestReportIsFilledOutWithTimeImprints _report;
        private readonly IFormTaskPackageFromPlainText _front;

        public EventCounterHandler(
            IHostApplicationLifetime applicationLifetime,
            ICacheManagerService cache,
            ITestOfComplexIntegrityMainServicee test,
            ITestReportIsFilledOutWithTimeImprints report,
            IFormTaskPackageFromPlainText front
            )
        {
            _cancellationToken = applicationLifetime.ApplicationStopping;
            _cache = cache;
            _test = test;
            _report = report;
            _front = front;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<EventCounterHandler>();

        private Timer _timer;
        private bool _timerCanBeStarted;
        private bool _handlerCallingsMergeCanBeCalled;
        private int _callingNumOfEventFrom;
        private int _currentChainSerialNum;

        private bool _isTestStarted; // can be removed

        public void EventCounterInit(ConstantsSet constantsSet)
        {
            _callingNumOfEventFrom = 0;
            _handlerCallingsMergeCanBeCalled = true;

            // ключ блокировки запуска реальной задачи после запуска теста (maybe it is needed to rename to _realTaskCanBeProcessed)
            _isTestStarted = false;

            _currentChainSerialNum = 0;

            // инициализация таймера (DoWork не сработает с нулевым счетчиком)
            //object state = (constantsSet, "the second parameter can be placed here");
            _timer = new Timer(DoWork, constantsSet, 0, Timeout.Infinite);
            _timerCanBeStarted = true;
        }

        public async Task<bool> IsCounterZeroReading(ConstantsSet constantsSet)
        {
            // можно повиснуть в методе и ждать положительного ответа
            // while с задержкой в 0,1 сек и ждать, когда задачи выполнятся

            int count = Volatile.Read(ref _callingNumOfEventFrom);

            // ещё один вариант атомарного считывания значения счётчика - но только проверка на конкретное число
            // если значение счётчика равно последнему параметру, то он меняется на средний параметр
            // (даже не представляю, как это можно использовать)
            //bool isCount0 = 0 == Interlocked.CompareExchange(ref _callingNumOfEventFrom, 0, 0);

            // счётчик нулевой, значит задачи не выполняются, можно возвращать разрешение на запуск тестов
            if (count == 0)
            {
                // 
                _isTestStarted = true;
                Logs.Here().Information("New real Tasks are blocked. _isTestStarted = {0}", _isTestStarted);
                return true;
            }

            int countTrackingStart = constantsSet.IntegerConstant.BackgroundDispatcherConstant.CountTrackingStart.Value; // 2
            // можно сделать отдельную переменную, специально для этого while
            int delayTimeForTest1 = constantsSet.IntegerConstant.IntegrationTestConstant.DelayTimeForTest1.Value; // 1000
            int timerIntervalInMilliseconds = constantsSet.TimerIntervalInMilliseconds.Value; // 5000
            int currentTimeToWaitZeroCount = 0;
            int totalTimeOfZeroCountWaiting = (int)(timerIntervalInMilliseconds * 2.001) / delayTimeForTest1;
            Logs.Here().Information("totalTimeOfZeroCountWaiting = {0}.", totalTimeOfZeroCountWaiting);

            // результаты -
            // третий вызов задачи приходит когда только начинается проверка счётчика,
            // надо передвинуть вызов на более позднее время - на момент возврата из IsCounterZeroReading, но ещё там

            // точно такая же конструкция используется ещё в одном месте, можно выделить в метод, но там все непросто
            while (count > 0)
            {
                Logs.Here().Information("Event count {0} > 0, changes will be waited {1} sec.", count, delayTimeForTest1);
                await Task.Delay(delayTimeForTest1);
                currentTimeToWaitZeroCount++;
                Logs.Here().Information("Current time of zero count waiting = {0} sec.", currentTimeToWaitZeroCount * delayTimeForTest1);

                if (currentTimeToWaitZeroCount > totalTimeOfZeroCountWaiting)
                {
                    // 
                    _isTestStarted = true;
                    Logs.Here().Information("New real Tasks are blocked. _isTestStarted = {0}", _isTestStarted);

                    // сбросить счётчик событий                
                    int lastCount = Interlocked.Exchange(ref _callingNumOfEventFrom, 0);
                    Logs.Here().Information("_callingNumOfEventFrom {0} was reset and count = {1}.", _callingNumOfEventFrom, lastCount);
                }
                count = Volatile.Read(ref _callingNumOfEventFrom);
                if (count > countTrackingStart - 1)
                {
                    // произошло событие запуска пакета реальных задач, надо ждать заново и проверять счётчик
                    currentTimeToWaitZeroCount = 0;
                }
            }

            // или за время ожидания закончатся все задачи и счётчик обнулится или
            // если есть только одна задача и она не собирается выполняться, через время счётчик обнулят и можно возвращать true

            Logs.Here().Information("count {0} has became zero and true will be returned.", count);
            return true;
        }

        // 
        private bool AddStageToProgressReport(ConstantsSet constantsSet, int currentChainSerialNum, long currentWorkStopwatch, int workActionNum = -1, bool workActionVal = false, string workActionName = "", string workActionDescription = "", int callingCountOfTheMethod = -1, [CallerMemberName] string currentMethodName = "")
        {
            bool isTestInProgress = _test.FetchIsTestInProgress();
            if (isTestInProgress)
            {
                TestReport.TestReportStage sendingTestTimingReportStage = new TestReport.TestReportStage()
                {
                    ChainSerialNumber = currentChainSerialNum,
                    TsWork = currentWorkStopwatch,
                    MethodNameWhichCalled = currentMethodName,
                    WorkActionNum = workActionNum,
                    WorkActionVal = workActionVal,
                    WorkActionName = workActionName,
                    WorkActionDescription = workActionDescription,
                    CallingCountOfWorkMethod = callingCountOfTheMethod
                };
                _ = _report.AddStageToTestTaskProgressReport(constantsSet, sendingTestTimingReportStage);
            }
            return isTestInProgress;
        }

        // методы (таймер тоже) не асинхронные и их ждут - наверное, можно работать параллельно
        public Task EventCounterOccurred(ConstantsSet constantsSet, string eventKey, int currentChainSerialNum, CancellationToken stoppingToken)
        {
            int countTrackingStart = constantsSet.IntegerConstant.BackgroundDispatcherConstant.CountTrackingStart.Value; // 2
            int countDecisionMaking = constantsSet.IntegerConstant.BackgroundDispatcherConstant.CountDecisionMaking.Value; // 6

            // тут будет проблема с множественным присвоением
            _currentChainSerialNum = currentChainSerialNum;

            // считать вызовы подписки и запустить таймер после первого (второго?) вызова
            int count = Interlocked.Increment(ref _callingNumOfEventFrom);
            Logs.Here().Information("Key {0} was received for the {1} time, count = {2}.", eventKey, _callingNumOfEventFrom, count);

            _ = AddStageToProgressReport(constantsSet, currentChainSerialNum, _test.FetchWorkStopwatch(), count, false, "count was Incremented", "", -1);

            // на втором вызове запускаем таймер на N секунд (второй вызов - это 2, а не 1)

            if (_timerCanBeStarted && count > countTrackingStart - 1)
            {
                Logs.Here().Information("Event count {0} == {1} was discovered.", count, countTrackingStart);
                _ = StartTimerOnce(constantsSet, currentChainSerialNum);
                _ = AddStageToProgressReport(constantsSet, currentChainSerialNum, _test.FetchWorkStopwatch(), count, _timerCanBeStarted, "StartTimerOnce calling has passed", "in if _timer && count = 2", -1);
            }

            if (count > countDecisionMaking - 1)
            {
                Logs.Here().Information("Event count {0} == {1} was discovered.", count, countDecisionMaking);
                int countForHandlerMergeOfCalling = count;
                // сразу же сбросить счётчик событий                
                count = Interlocked.Exchange(ref _callingNumOfEventFrom, 0);
                Logs.Here().Information("_callingNumOfEventFrom {0} was reset and count = {1}.", _callingNumOfEventFrom, count);

                _ = HandlerMergeOfCalling(constantsSet, currentChainSerialNum);
                _ = AddStageToProgressReport(constantsSet, currentChainSerialNum, _test.FetchWorkStopwatch(), countForHandlerMergeOfCalling, false, "HandlerMergeOfCalling calling has passed", "in if count = 6", -1);

                Logs.Here().Information("EventCounter was elapsed.");
            }

            return Task.CompletedTask;
        }

        // обработчик слияния вызовов по счётчику и таймеру - может, CounterAndTimerCallMergeHandler - ?
        private async Task HandlerMergeOfCalling(ConstantsSet constantsSet, int currentChainSerialNum)
        {
            // слияние вызовов обработчика из таймера и из счётчика
            Logs.Here().Information("HandlerMergeOfCalling was started.");


            // остановить и сбросить таймер (не очищать?)
            Logs.Here().Information("Timer will be stopped.");
            _ = StopTimer(_cancellationToken);

            // предусмотреть блокировку повторного вызова метода слияния (не повторного, а сдвоенного - от счетчика и таймера одновременно)
            while (!_handlerCallingsMergeCanBeCalled)
            {
                Logs.Here().Warning("   ********** HandlerCallingsMerge double call was detected! ********** ");
                _ = AddStageToProgressReport(constantsSet, currentChainSerialNum, _test.FetchWorkStopwatch(), - 1, _handlerCallingsMergeCanBeCalled, "_handlerCallingsMergeCanBeCalled", "in the while - HandlerCallingsMerge double call was detected!", -1);

                await Task.Delay(100);
            }

            _handlerCallingsMergeCanBeCalled = false;

            // тут можно возвращать true из обработчика - с await, это будет означать, что он освободился и готов принять событие во второй поток
            // _isTestInProgress убрали из вызова, фронт класс узнает его самостоятельно
            _handlerCallingsMergeCanBeCalled = _front.HandlerCallingsDistributor(constantsSet, _currentChainSerialNum, _cancellationToken);
            _ = AddStageToProgressReport(constantsSet, currentChainSerialNum, _test.FetchWorkStopwatch(), - 1, _handlerCallingsMergeCanBeCalled, "_handlerCallingsMergeCanBeCalled", "HandlerCallingsDistributor calling has passed", -1);

            Logs.Here().Information("HandlerCallingDistributore returned calling unblock. {@F}", new { Flag = _handlerCallingsMergeCanBeCalled });
        }

        private Task StartTimerOnce(ConstantsSet constantsSet, int currentChainSerialNum)
        {

            if (_timerCanBeStarted)
            {
                _ = AddStageToProgressReport(constantsSet, currentChainSerialNum, _test.FetchWorkStopwatch(), - 1, _timerCanBeStarted, "_timerCanBeStarted", "in if _timerCanBeStarted");
                _timerCanBeStarted = false;
                int timerIntervalInMilliseconds = constantsSet.TimerIntervalInMilliseconds.Value;
                Logs.Here().Information("Timer will be started for {0} msec.", timerIntervalInMilliseconds);

                // таймер с однократной сработкой через интервал timerIntervalInMilliseconds
                _timer?.Change(timerIntervalInMilliseconds, Timeout.Infinite);
                //_timer = new Timer(DoWork, constantsSet, timerIntervalInMilliseconds, Timeout.Infinite);

                Logs.Here().Information("Timer was started for {0} msec, {@T}.", timerIntervalInMilliseconds, new { TimerCanBeStarted = _timerCanBeStarted });
            }
            return Task.CompletedTask;
        }

        private void DoWork(object state)
        {
            // сюда попали, когда вышло время ожидания по таймеру

            ConstantsSet constantsSet = (ConstantsSet)state;
            int countTrackingStart = constantsSet.IntegerConstant.BackgroundDispatcherConstant.CountTrackingStart.Value; // 2
            var count = Volatile.Read(ref _callingNumOfEventFrom);

            _ = AddStageToProgressReport(constantsSet, _currentChainSerialNum, _test.FetchWorkStopwatch(), count, false, "count", "Timer starts DoWork", -1);

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

            _ = HandlerMergeOfCalling(constantsSet, _currentChainSerialNum);
            _ = AddStageToProgressReport(constantsSet, _currentChainSerialNum, _test.FetchWorkStopwatch(), count, false, "count has been reset", "HandlerMergeOfCalling calling has passed", -1);

            Logs.Here().Information("HandlerMergeOfCalling calling has passed.");
        }

        private Task StopTimer(CancellationToken stoppingToken)
        {
            _timerCanBeStarted = true;

            Logs.Here().Information("Timer is stopping.");

            _timer?.Change(Timeout.Infinite, 0);

            Logs.Here().Information("Timer state {@T}.", new { TimerCanBeStarted = _timerCanBeStarted });

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
