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

// план работ -

namespace BackgroundDispatcher.Services
{
    public interface IEventCounterHandler
    {
        public void EventCounterInit(ConstantsSet constantsSet);
        public Task<bool> IsCounterZeroReading(ConstantsSet constantsSet);
        public bool IsTestStarted();
        public void TestIsFinished();
        public Task EventCounterOccurred(ConstantsSet constantsSet, string eventKey, CancellationToken stoppingToken);
        public void Dispose();
    }

    public class EventCounterHandler : IEventCounterHandler
    {
        private readonly CancellationToken _cancellationToken;
        private readonly ICacheManagerService _cache; // TO REMOVE
        private readonly IIntegrationTestService _test;
        private readonly IFormTaskPackageFromPlainText _front;

        public EventCounterHandler(
            IHostApplicationLifetime applicationLifetime,
            ICacheManagerService cache,
            IIntegrationTestService test,
            IFormTaskPackageFromPlainText front
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
        private bool _isTestStarted;

        public void EventCounterInit(ConstantsSet constantsSet)
        {
            _callingNumOfEventFrom = 0;
            _handlerCallingsMergeCanBeCalled = true;

            // ключ блокировки запуска реальной задачи после запуска теста (maybe it is needed to rename to _realTaskCanBeProcessed)
            _isTestStarted = false;

            // инициализация таймера (DoWork не сработает с нулевым счетчиком)
            _timer = new Timer(DoWork, constantsSet, 0, Timeout.Infinite);
            _timerCanBeStarted = true;
        }

        // bool isTestNotInProgress = !_isTestInProgress;
        // план -
        // 1 разделить подписки
        // 2 добавить блокировку в настоящей подписке на время теста
        // настоящий ключ с задачей(если был один) придётся выбросить, всё равно он один никуда не годный
        // а если будет два, то по таймеру они выполнятся перед тестом
        // 3 добавить ожидание тестом выполняемой задачи, но не более таймера + один цикл ожидания сверху
        // потом что-то решать(пока пусть пользователь решает)
        // потом можно выяснять состояние счётчика и если навсегда остался один, принудительно обнулить
        // и не забыть сразу же очистить ключ постановки задач
        // ситуация со счётчиком = 1
        // если при проверке счётчика на ноль выяснится, что он больше нуля, но меньше порога срабатывания таймера
        // (2 sec - кстати, посмотреть насчёт переименования)
        // и продолжается это достаточно долго
        // (время таймера слишком много, надо подумать, какое выбрать - может специально для этого завести константу)
        // то счётчик надо сбросить, а тест разрешить
        // но прямо там сбрасывать счётчик нехорошо - незащищённое место
        // надо вернуться в запуск теста, заблокировать выполнение настоящих задач и только тогда сбросить счётчик
        // и можно не разбираться, была там единица или нет, а сбрасывать всегда - тоже методом из соседнего класса для доступа к счётчику
        // при дальнейшем углублении теста показывать этапы прохождения
        
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

        public bool IsTestStarted()
        {
            return _isTestStarted;
        }
        
        public void TestIsFinished()
        {
            _isTestStarted = false;
        }

        // методы (таймер тоже) не асинхронные и их ждут - наверное, можно работать параллельно
        public Task EventCounterOccurred(ConstantsSet constantsSet, string eventKey, CancellationToken stoppingToken)
        {
            // считать вызовы подписки и запустить таймер после первого (второго?) вызова
            int count = Interlocked.Increment(ref _callingNumOfEventFrom);
            Logs.Here().Information("Key {0} was received for the {1} time, count = {2}.", eventKey, _callingNumOfEventFrom, count);

            // на втором вызове запускаем таймер на N секунд (второй вызов - это 2, а не 1)

            int countTrackingStart = constantsSet.IntegerConstant.BackgroundDispatcherConstant.CountTrackingStart.Value; // 2
            int countDecisionMaking = constantsSet.IntegerConstant.BackgroundDispatcherConstant.CountDecisionMaking.Value; // 6

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
            Logs.Here().Information("HandlerMergeOfCalling was started.");

            // остановить и сбросить таймер (не очищать?)
            Logs.Here().Information("Timer will be stopped.");
            _ = StopTimer(_cancellationToken);

            // true - to call temp test
            // создание третьей задачи, когда две только уехали на обработку по таймеру - как поведёт себя вызов теста в этот момент
            // рассмотреть два варианта - вызов теста до появления третьей задачи и после
            // по идее в первом варианте третья задача должна остаться проигнорироаанной
            // а во втором - тест должен отложиться на 10 секунд и потом задача должна удалиться
            bool tempTestOf3rdTaskAdded = false;

            // tartTask3beforeTest = true - тест должен отложиться на 10 секунд и потом одиночная задача должна удалиться
            // tartTask3beforeTest = false - третья задача должна остаться проигнорироаанной, а тест выполниться сразу же, без ожидания 10 сек

            if (tempTestOf3rdTaskAdded)
            {
                bool startTask3beforeTest = false;
                bool checkValue = await _test.TempTestOf3rdTaskAdded(constantsSet, tempTestOf3rdTaskAdded, startTask3beforeTest);
                Logs.Here().Information("to read value for awaiting when keys will be written - checkValue = {0})", checkValue);
            }

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
            // _isTestInProgress убрали из вызова, фронт класс узнает его самостоятельно
            _handlerCallingsMergeCanBeCalled = _front.HandlerCallingsDistributor(constantsSet, _cancellationToken);
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
            Logs.Here().Information("HandlerMergeOfCalling was called.");
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
