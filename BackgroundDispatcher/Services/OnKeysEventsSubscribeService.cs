﻿using System;
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
        private bool _timerCanBeStarted;
        private bool _handlerCallingsMergeCanBeCalled;
        private int _callingNumOfEventFrom;

        // 1 разблокировать вызов для следующего забега
        // 2 не вызывается следующий метод - может из-за несоответствия типов
        // 3 сделать встроенный интеграционный тест

        // 1 - разблокировать вызов, когда освободится обработчик
        // при заблокированном вызове можно отключать счётчик и таймер - всё равно никто не ждёт
        // счётчик можно оставить - потом проверить, чего там накопилось, если достаточно, сразу запустить (или таймер или обработчик)
        // тогда в подписке на событие дополнительный if, обходящий все проверки
        // а после возврата из вызова обработчика (с ожиданием), разблокировать вызов и сразу как-то проверить счётчик
        // скопировать решение из бэк-сервера - с while или как там сделано
        // типа, можно крутиться вокруг вызова обработчика, пока не разблокируется вызов и не обнулится счётчик

        // 2 - добавить логов в следующем классе

        // 3 - добавить подписку, запускающую интеграционный тест в класс обработки подписок
        // две ситуации, которые трудно воспроизвести руками - 
        // 1 одновременная сработка таймера и счётчика (при подходе к времени таймера сделать три сработки счётчика, чтобы попасть на двойной вызов)
        // можно настраивать количества в счётчике до сработки и время таймера
        // 2 много счётчика и накопилась вторая порция, когда обработчик ещё не освободился
        // рассмотреть вариант множественного вызова обработчика - со стандартным методом захвата события из очереди (с удалением)

        // связать данные интеграционного теста с веб-интерфейсом - получать команду на запуск и передавать данные о текущей ситуации
        // например, накопление счётчика, состояние таймера, вызов обработчика - появление двойного вызова
        // в случае множественного обработчика - отслеживание состояния каждого инстанса

        // поправки на множественные обработчики
        // блокировка двойного вызова только в слиянии
        // останавливать только таймер, но не счётчик
        // при разблокировки вызова проверять счётчик




        public void SubscribeOnEventFrom(ConstantsSet constantsSet)
        {
            // здесь сделать такой же механизм с блокировкой и проверкой пропущенных, как у бэк-сервера
            // нет, здесь свой механизм - с накоплением и ожиданием по таймеру

            string eventKey = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
            KeyEvent eventCmd = constantsSet.EventCmd; // HashSet
            _callingNumOfEventFrom = 0;
            _handlerCallingsMergeCanBeCalled = true;

            // инициализация таймера (DoWork не сработает с нулевым счетчиком)
            _timer = new Timer(DoWork, constantsSet, 0, Timeout.Infinite);
            _timerCanBeStarted = true;

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

                    _ = EventCounter(constantsSet, _cancellationToken);
                }
            });

            string eventKeyCommand = $"Key = {eventKey}, Command = {eventCmd}";
            _logger.LogInformation("You subscribed on event - {EventKey}.", eventKeyCommand);
            _logger.LogInformation("To start the front emulation please send from Redis console the following command - \n{_}{0} {1} count NN (NN - packages count).", "      ", eventCmd, eventKey);
        }

        public Task EventCounter(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            // на втором вызове запускаем таймер на N секунд (второй вызов - это 2, а не 1)

            int countTrackingStart = 2;
            int countDecisionMaking = 6;

            var count = Volatile.Read(ref _callingNumOfEventFrom);

            if (count > countTrackingStart - 1)
            {
                Logs.Here().Information("Event count {0} == {1} was discovered.", count, countTrackingStart);
                _ = StartTimerOnce(constantsSet, _cancellationToken);
            }

            //stopWatch.Stop();
            //TimeSpan ts = stopWatch.Elapsed; // Get the elapsed time as a TimeSpan value.

            if (count > countDecisionMaking - 1)
            {
                Logs.Here().Information("Event count {0} == {1} was discovered.", count, countDecisionMaking);
                // сразу же сбросить счётчик событий                
                count = Interlocked.Exchange(ref _callingNumOfEventFrom, 0);
                Logs.Here().Information("_callingNumOfEventFrom {0} was reset and count = {1}.", _callingNumOfEventFrom, count);

                _ = HandlerCallingsMerge(constantsSet);

                Logs.Here().Information("EventCounter was elapsed.");
            }

            return Task.CompletedTask;
        }

        private async Task HandlerCallingsMerge(ConstantsSet constantsSet)
        {
            // слияние вызовов обработчика из таймера и из счётчика
            Logs.Here().Information("HandlerCallingsMerge was started.");

            // остановить и сбросить таймер (не очищать?)
            Logs.Here().Information("Timer will be stopped.");
            _ = StopTimer(_cancellationToken);

            // предусмотреть блокировку повторного вызова метода слияния (не повторного, а сдвоенного)
            if (_handlerCallingsMergeCanBeCalled)
            {
                _handlerCallingsMergeCanBeCalled = false;

                // тут можно возвращать true из обработчика - с await, это будет означать, что он освободился и готов принять во второй поток
                _handlerCallingsMergeCanBeCalled = await _front.HandlerCallingDistributore(constantsSet, _cancellationToken);
                Logs.Here().Information("HandlerCallingDistributore returned calling unblock. {@F}", new { Flag = _handlerCallingsMergeCanBeCalled });
            }
            else
            {
                Logs.Here().Warning("   ********** HandlerCallingsMerge double call was detected! ********** ");
            }

            //return Task.CompletedTask;
        }

        public Task StartTimerOnce(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            if (_timerCanBeStarted)
            {
                _timerCanBeStarted = false;
                //_logger.LogInformation("Timed Hosted Service running.");
                //double nSec = 15;
                int timerIntervalInMilliseconds = constantsSet.TimerIntervalInMilliseconds.Value;
                Logs.Here().Information("Timer will be started for {0} msec.", timerIntervalInMilliseconds);

                // как остановить таймер по stoppingToken?
                //_timer = new Timer(DoWork, constantsSet, TimeSpan.Zero, TimeSpan.FromSeconds(nSec));
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
            // если в это время закончится счётчик, то ничего делать уже не надо - пойдёт вызов по счётчику
            // поэтому перед вызовом обработчика ещё раз проверить счётчик
            // хотя это и не гарантирует отсутствия двойного вызова
            // но ничего, в обработчике закроем стандартные двери за первым вошедшим

            ConstantsSet constantsSet = (ConstantsSet)state;
            int countTrackingStart = 2;
            var count = Volatile.Read(ref _callingNumOfEventFrom);
            Logs.Here().Information("_callingNumOfEventFrom {0} was Volatile.Read and count = {1}.", _callingNumOfEventFrom, count);

            if (count < countTrackingStart)
            {
                return;
            }

            Logs.Here().Information("Timer called DoWork.");

            // сразу же сбросить счётчик событий
            count = Interlocked.Exchange(ref _callingNumOfEventFrom, 0);
            Logs.Here().Information("_callingNumOfEventFrom {0} was reset and count = {1}.", _callingNumOfEventFrom, count);

            _ = HandlerCallingsMerge(constantsSet);
            Logs.Here().Information("HandlerCallingsMerge was called.");
        }

        public Task StopTimer(CancellationToken stoppingToken)
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