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
    public interface IOnKeysEventsSubscribeService
    {
        public void SubscribeOnEventFrom(ConstantsSet constantsSet);
        public void SubscribeOnTestEvent(ConstantsSet constantsSet);
        public void Dispose();
    }

    public class OnKeysEventsSubscribeService : IOnKeysEventsSubscribeService
    {

        private readonly CancellationToken _cancellationToken;
        private readonly ILogger<OnKeysEventsSubscribeService> _logger;
        private readonly ICacheManageService _cache;
        private readonly IKeyEventsProvider _keyEvents;
        private readonly IIntegrationTestService _test;
        private readonly ITaskPackageFormationFromPlainText _front;

        public OnKeysEventsSubscribeService(
            IHostApplicationLifetime applicationLifetime,
            ILogger<OnKeysEventsSubscribeService> logger,
            ICacheManageService cache,
            IKeyEventsProvider keyEvents,
            IIntegrationTestService test,
            ITaskPackageFormationFromPlainText front
            )
        {
            _cancellationToken = applicationLifetime.ApplicationStopping;
            _logger = logger;
            _cache = cache;
            _keyEvents = keyEvents;
            _test = test;
            _front = front;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<OnKeysEventsSubscribeService>();
        private Timer _timer;
        private bool _timerCanBeStarted;
        private bool _handlerCallingsMergeCanBeCalled;
        private int _callingNumOfEventFrom;
        private bool _isTestInProgress;

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
        // при разблокировке вызова проверять счётчик

        // можно при запуске теста модифицировать ключ from, чтобы не могли проходить настоящие задачи
        // но лучше проверять содержимое поля - у реальной задачи и теста оно различается
        // и надо предусмотреть ответ контроллеру, что вас много, а я одна
        // перед стартом теста надо проверить, не занят ли сервер реальной задачей
        // для тестирования этой ситуации можно увеличить время выполнения реальной задачи, чтобы успеть запустить тест


        // подписка на ключ создания задачи (загрузки книги)
        public void SubscribeOnEventFrom(ConstantsSet constantsSet)
        {
            string eventKeyFrom = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
            KeyEvent eventCmd = constantsSet.EventCmd; // HashSet
            _callingNumOfEventFrom = 0;
            _handlerCallingsMergeCanBeCalled = true;
            // по умолчанию ставим переключатель Test/Work в положение работа
            _isTestInProgress = false;

            // инициализация таймера (DoWork не сработает с нулевым счетчиком)
            _timer = new Timer(DoWork, constantsSet, 0, Timeout.Infinite);
            _timerCanBeStarted = true;

            _keyEvents.Subscribe(eventKeyFrom, (key, cmd) => // async
            {
                if (cmd == eventCmd)
                {
                    // считать вызовы подписки и запустить таймер после первого (второго?) вызова
                    int count = Interlocked.Increment(ref _callingNumOfEventFrom);
                    Logs.Here().Information("Key {0} was received for the {1} time, count = {2}.\n", eventKeyFrom, _callingNumOfEventFrom, count);

                    _ = EventCounter(constantsSet, _cancellationToken);
                }
            });

            string eventKeyCommand = $"Key = {eventKeyFrom}, Command = {eventCmd}";
            _logger.LogInformation("You subscribed on event - {EventKey}.", eventKeyCommand);
            _logger.LogInformation("To start the front emulation please send from Redis console the following command - \n{_}{0} {1} count NN (NN - packages count).", "      ", eventCmd, eventKeyFrom);
        }

        public Task EventCounter(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            // на втором вызове запускаем таймер на N секунд (второй вызов - это 2, а не 1)

            int countTrackingStart = constantsSet.IntegerConstant.BackgroundDispatcherConstant.CountTrackingStart.Value; // 2
            int countDecisionMaking = constantsSet.IntegerConstant.BackgroundDispatcherConstant.CountDecisionMaking.Value; // 6

            var count = Volatile.Read(ref _callingNumOfEventFrom);

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

        // not used
        private async Task<bool> IsTestStartedAndAllowed(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            // надо проверить значение поля ключа From - если там bookPlainText_FieldPrefixGuid, то это работа, а если "count", то тест
            // для этого надо удалять отработанные поля в более поздних методах
            // проверка - достать все поля из ключа
            // если поле только одно и соответствует тесту, то можно переключать в тест
            // если есть что-то ещё, то обработчик занят реальной задачей, тогда сообщить, что проведение теста запрещено законом
            // поле теста тоже надо удалять, можно прямо здесь, после принятия решения

            // всё не так просто - ключ генерируется очень часто (при тестах или интенсивной работе)
            // придётся подменять ключ From - при запуске теста настоящий ключ From заблокировать флагом

            string eventKeyFrom = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
            IDictionary<string, string> tasks = await _cache.FetchHashedAllAsync<string>(eventKeyFrom);
            int tasksCount = tasks.Count;
            if(tasksCount == 1)
            {
                foreach(var t in tasks)
                {
                    (string field, string value) = t;
                    if (field == "count")
                    {
                        // тут удалить поле
                        // а в методе, запустившем тест, проверить удаление поля - что тест начал работать
                        bool isFieldDeleted = await _cache.DelFieldAsync(eventKeyFrom, field);
                        return true;
                    }
                }
                return false;
            }
            return false;
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
            Logs.Here().Information("_callingNumOfEventFrom {0} was Volatile.Read and count = {1}.", _callingNumOfEventFrom, count);

            // проверка для пропуска инициализации таймера
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

        // подписка на команду на запуск тестов
        public void SubscribeOnTestEvent(ConstantsSet constantsSet)
        {
            string eventKey = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            KeyEvent eventCmd = constantsSet.EventCmd; // HashSet

            _keyEvents.Subscribe(eventKey, async (key, cmd) => 
            {
                if (cmd == eventCmd)
                {
                    bool isTestReadyToStart = !_isTestInProgress;
                    if (isTestReadyToStart)
                    {
                        Logs.Here().Information("Key {0} was received, integration test starts. \n", eventKey);
                        // тут заблокировать повторное событие до окончания теста
                        // общий флаг запуска теста и блокировки повторного запуска                    
                        // ставим переключатель в положение тест
                        _isTestInProgress = true;
                        Logs.Here().Information("Is test in progress state = {0}, integration test started. \n", _isTestInProgress);
                        // после окончания теста снять блокировку
                        _isTestInProgress = await _test.IntegrationTestStart(constantsSet, _cancellationToken);
                        Logs.Here().Information("Is test in progress state = {0}, integration test finished. \n", _isTestInProgress);



                        // и ещё не забыть проверить состояние рабочего ключа - там могли скопиться задачи
                        // и для этого тоже нужен тест...



                    }
                }
            });

            string eventKeyCommand = $"Key = {eventKey}, Command = {eventCmd}";
            _logger.LogInformation("You subscribed on event - {EventKey}.", eventKeyCommand);
            _logger.LogInformation("To start the front emulation please send from Redis console the following command - \n{_}{0} {1} count NN (NN - packages count).", "      ", eventCmd, eventKey);
        }

    }
}
