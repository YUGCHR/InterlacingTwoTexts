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
// 1 разблокировать вызов для следующего забега
// 2 не вызывается следующий метод - может из-за несоответствия типов
// 3 сделать встроенный интеграционный тест
//
// 1 - разблокировать вызов, когда освободится обработчик
// при заблокированном вызове можно отключать счётчик и таймер - всё равно никто не ждёт
// счётчик можно оставить - потом проверить, чего там накопилось, если достаточно, сразу запустить (или таймер или обработчик)
// тогда в подписке на событие дополнительный if, обходящий все проверки
// а после возврата из вызова обработчика (с ожиданием), разблокировать вызов и сразу как-то проверить счётчик
// скопировать решение из бэк-сервера - с while или как там сделано
// типа, можно крутиться вокруг вызова обработчика, пока не разблокируется вызов и не обнулится счётчик
//
// 3 - добавить подписку, запускающую интеграционный тест в класс обработки подписок
// две ситуации, которые трудно воспроизвести руками - 
// 1 одновременная сработка таймера и счётчика (при подходе к времени таймера сделать три сработки счётчика, чтобы попасть на двойной вызов)
// можно настраивать количества в счётчике до сработки и время таймера
// 2 много счётчика и накопилась вторая порция, когда обработчик ещё не освободился
// рассмотреть вариант множественного вызова обработчика - со стандартным методом захвата события из очереди (с удалением)
//
// связать данные интеграционного теста с веб-интерфейсом - получать команду на запуск и передавать данные о текущей ситуации
// например, накопление счётчика, состояние таймера, вызов обработчика - появление двойного вызова
// в случае множественного обработчика - отслеживание состояния каждого инстанса
//
// поправки на множественные обработчики
// блокировка двойного вызова только в слиянии
// останавливать только таймер, но не счётчик
// при разблокировке вызова проверять счётчик
//
// можно при запуске теста модифицировать ключ from, чтобы не могли проходить настоящие задачи
// но лучше проверять содержимое поля - у реальной задачи и теста оно различается
// 
//
// и надо предусмотреть ответ контроллеру, что вас много, а я одна
// перед стартом теста надо проверить, не занят ли сервер реальной задачей
// для тестирования этой ситуации можно увеличить время выполнения реальной задачи, чтобы успеть запустить тест
//
// проверить вариант, когда одна реальная задача добавляется после начала выполнения двух предыдущих - когда идёт ожидание обнуления счётчика
// можно ли охватить это ещё дополнительным тестом, хотя бы временным или это перебор?
// можно сгенерировать ключ реальной задачи из кода - добавить времянку для тестирования этого варианта
// и не один раз, а пару раз - для наглядности и потом не создавать - пусть запустится тест
// и после запуска теста создать ещё один реальный ключ или даже несколько - добавить времянку в цикл создания тестовых ключей
// проверить, что прохождение реальных задач заблокировано
// и посмотреть, как подобрать их после окончания теста и выполнить, если хватает

namespace BackgroundDispatcher.Services
{
    public interface IOnKeysEventsSubscribeService
    {
        public Task SubscribingPlan(ConstantsSet constantsSet);
    }

    public class OnKeysEventsSubscribeService : IOnKeysEventsSubscribeService
    {
        private readonly CancellationToken _cancellationToken;
        private readonly IKeyEventsProvider _keyEvents;
        private readonly ITestOfComplexIntegrityMainServicee _test;
        private readonly ITestReportIsFilledOutWithTimeImprints _report;
        private readonly IEventCounterHandler _count;
        private readonly ITestResultsAssertServerHealth _assert;

        public OnKeysEventsSubscribeService(
            IHostApplicationLifetime applicationLifetime,
            IKeyEventsProvider keyEvents,
            ITestOfComplexIntegrityMainServicee test,
            ITestReportIsFilledOutWithTimeImprints report,
            IEventCounterHandler count,
            ITestResultsAssertServerHealth assert
            )
        {
            _cancellationToken = applicationLifetime.ApplicationStopping;
            _keyEvents = keyEvents;
            _test = test;
            _report = report;
            _count = count;
            _assert = assert;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<OnKeysEventsSubscribeService>();

        private bool _isTestInProgressAlready;
        private int _callingNumOfEventKeyFrom;

        public async Task SubscribingPlan(ConstantsSet constantsSet)
        {
            string eventKeyFrom = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
            KeyEvent eventCmd = constantsSet.EventCmd; // HashSet            

            string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            string cafeKey = constantsSet.Prefix.BackgroundDispatcherPrefix.EventKeyFrontGivesTask.Value; // key-event-front-server-gives-task-package

            // временное удаление рабочих ключей для тестирования (а может и постоянное)
            bool eventKeyFromWasDeleted = await _test.RemoveWorkKeyOnStart(eventKeyFrom);
            bool cafeKeyWasDeleted = await _test.RemoveWorkKeyOnStart(cafeKey);
            Logs.Here().Information("Keys {0} and {1} were deleted successfully - {2} / {3}.", eventKeyFrom, cafeKey, eventKeyFromWasDeleted, cafeKeyWasDeleted);

            // инициализируем поля и таймер в классе EventCounterHandler
            _count.EventCounterInit(constantsSet);

            // ключ блокировки повторного запуска теста до окончания уже запущенного
            _isTestInProgressAlready = false;

            _callingNumOfEventKeyFrom = 0;

            // подписка на ключ создания задачи (загрузки книги)
            SubscribeOnEventFrom(constantsSet, eventKeyFrom, eventCmd);

            // подписка на ключ для старта тестов
            SubscribeOnTestEvent(constantsSet, eventKeyTest, eventCmd);

            char separatorUnit = '-';
            //string messageText = "To start Test please dispatch from Redis console the following command - ";
            string testConsoleCommand = $"127.0.0.1:6379> hset {eventKeyTest} test 1";
            (string frameSeparator1, string inFrameTextMessage) = GenerateMessageInFrame.CreateMeassageInFrame(separatorUnit, testConsoleCommand);
            Logs.Here().Information("To start Test please type from Redis console the following command - \n {0} \n {1} \n {2}", frameSeparator1, inFrameTextMessage, frameSeparator1);
        }

        // 
        private bool AddStageToProgressReport(ConstantsSet constantsSet, int currentChainSerialNum, long currentWorkStopwatch, int workActionNum = -1, bool workActionVal = false, string workActionName = "", int controlPointNum = 0, int callingCountOfTheMethod = -1, [CallerMemberName] string currentMethodName = "")
        {
            bool isTestInProgress = _test.FetchIsTestInProgress();
            // проверили, тест сейчас или нет и, если да, обратиться за серийным номером цепочки и записать шаг отчета
            if (isTestInProgress)
            {
                // ещё можно получать и записывать номер потока, в котором выполняется этот метод
                TestReport.TestReportStage sendingTestTimingReportStage = new TestReport.TestReportStage()
                {
                    ChainSerialNumber = currentChainSerialNum,
                    TsWork = currentWorkStopwatch,
                    MethodNameWhichCalled = currentMethodName,
                    WorkActionNum = workActionNum,
                    WorkActionVal = workActionVal,
                    WorkActionName = workActionName,
                    ControlPointNum = controlPointNum,
                    CallingCountOfWorkMethod = callingCountOfTheMethod
                };
                _ = _report.AddStageToTestTaskProgressReport(constantsSet, sendingTestTimingReportStage);
                //Logs.Here().Debug("AddStageToTestTaskProgressReport calling has passed, currentChainSerialNum = {0}.", currentChainSerialNum);
            }
            return isTestInProgress;
        }

        // подписка на ключ создания задачи (загрузки книги)
        private void SubscribeOnEventFrom(ConstantsSet constantsSet, string eventKeyFrom, KeyEvent eventCmd)
        {
            int currentChainSerialNum = -1;
            //long currentWorkStopwatch = -1;

            _keyEvents.Subscribe(eventKeyFrom, (key, cmd) => // async
            {
                if (cmd == eventCmd)
                {
                    Logs.Here().Information("*** 166 Step 1 - Action FromEntity was called at time {0}.", _test.FetchWorkStopwatch());

                    int lastCountStart = Interlocked.Increment(ref _callingNumOfEventKeyFrom);

                    Logs.Here().Information("*** 170 Step 2 - Number of this FromEntity = {0} at time {1}.", lastCountStart, _test.FetchWorkStopwatch());

                    // можно проверять поле работы теста _isTestInProgressAlready и по нему ходить за серийным номером
                    // можно перенести генерацию серийного номера цепочки прямо сюда - int count = Interlocked.Increment(ref _currentChainSerialNum);
                    currentChainSerialNum = _test.FetchAssignedChainSerialNum(lastCountStart);

                    Logs.Here().Information("*** 176 Step 3 - FromEntity No: {0} fetched chain No: {1} at time {2}.", lastCountStart, currentChainSerialNum, _test.FetchWorkStopwatch());

                    int controlPointNum1 = 1;
                    bool result = AddStageToProgressReport(constantsSet, currentChainSerialNum, _test.FetchWorkStopwatch(), -1, false, eventKeyFrom, controlPointNum1, lastCountStart);
                    
                    Logs.Here().Information("*** 181 Step 4 - FromEntity No: {0} called AddStage and chain is still {1} at time {2}.", lastCountStart, currentChainSerialNum, _test.FetchWorkStopwatch());

                    //Logs.Here().Information("*** 183 *** - FromEntity No: {0} will call Counter in chain No: {1} at time {2}.", lastCountStart, currentChainSerialNum, _test.FetchWorkStopwatch());
                    
                    _ = _count.EventCounterOccurred(constantsSet, eventKeyFrom, currentChainSerialNum, lastCountStart);

                    Logs.Here().Information("*** 187 Step 5 - FromEntity No: {0} called CounterOccurred and chain is still {1} at time {2}.", lastCountStart, currentChainSerialNum, _test.FetchWorkStopwatch());

                    int lastCountEnd = Interlocked.Decrement(ref _callingNumOfEventKeyFrom);
                }
            });
            Logs.Here().Information("Subscription on event key {0} was registered", eventKeyFrom);
        }

        // подписка на ключ кафе и по событию сообщать тестам
        // надо же, чтобы на событие кафе реагировало только при проведении теста
        // можно сделать поле класса, управляемое из тестов - при старте ставить в true и потом выключать
        // или отключать подписку, когда нет тестов - что более правильно
        // void Unsubscribe(string key);
        // соответственно, запуск подписки делать тоже из тестов, а отсюда убрать
        // всё хорошо, только отсюда ходят в тесты, а наоборот нет
        // можно запускать эту подписку из сработавшей подписки на ключ теста
        private void SubscribeOnEventСafeKey(ConstantsSet constantsSet, string cafeKey, KeyEvent eventCmd)
        {
            bool eventCafeIsNotExisted = true;
            _keyEvents.Subscribe(cafeKey, async (key, cmd) => // 
            {
                if (cmd == eventCmd && eventCafeIsNotExisted)
                {
                    eventCafeIsNotExisted = false;
                    Logs.Here().Information("Event Cafe occurred, subscription is blocked.");

                    eventCafeIsNotExisted = await _assert.EventCafeOccurred(constantsSet, _cancellationToken);

                    Logs.Here().Information("Event Cafe was processed, subscription is unblocked, eventCafeIsNotExisted - {0}", eventCafeIsNotExisted);
                }
            });
            Logs.Here().Information("Subscription on event key {0} was registered", cafeKey);
        }

        // подписка на команду на запуск тестов
        // при дальнейшем углублении теста показывать этапы прохождения
        private void SubscribeOnTestEvent(ConstantsSet constantsSet, string eventKeyTest, KeyEvent eventCmd)
        {
            _keyEvents.Subscribe(eventKeyTest, async (key, cmd) =>
            {
                if (cmd == eventCmd && !_isTestInProgressAlready)
                {
                    // тут заблокировать повторное событие до окончания теста
                    _isTestInProgressAlready = true;
                    Logs.Here().Information("Key {0} was received, integration test starts. _isTestInProgressAlready = {1} \n", eventKeyTest, _isTestInProgressAlready);

                    // ещё проверить счётчик и если не нулевой, ждать обнуления
                    // за ним придётся ходить в следующий класс
                    // если счётчик не нулевой, значит там обрабатывается настоящая задача и надо ждать

                    // показать лог получения ключа на запуск теста

                    // можно повиснуть на методе и ждать положительного ответа
                    Logs.Here().Information("isZeroCount will start to check zero counter state.");
                    bool isTestStarted = await _count.IsCounterZeroReading(constantsSet);
                    Logs.Here().Information("isZeroCount returned {0}.", isTestStarted);

                    // здесь (должно быть) всегда - счётчик задач нулевой, новые задачи заблокированы

                    // тут можно безопасно сбросить счётчик, только желательно его ещё раз проверить и так далее
                    // счётчик уже сброшен и новая задача заблокирована возвратом
                    string cafeKey = constantsSet.Prefix.BackgroundDispatcherPrefix.EventKeyFrontGivesTask.Value; // key-event-front-server-gives-task-package
                    SubscribeOnEventСafeKey(constantsSet, cafeKey, eventCmd);
                    Logs.Here().Information("Subscription on Event key {0} was done.", cafeKey);

                    Logs.Here().Information("Is test in progress state = {0}, integration test started.", _isTestInProgressAlready);
                    // после окончания теста снять блокировку
                    _isTestInProgressAlready = await _test.IntegrationTestStart(constantsSet, _cancellationToken);
                    Logs.Here().Debug("Is test in progress state = {0}, integration test finished.", _isTestInProgressAlready);

                    _keyEvents.Unsubscribe(cafeKey);
                    Logs.Here().Debug("Key {0} was unsubscribed.", cafeKey);

                    // в самом конце тестов показываем отчёт о временах прохождения контрольных точек (или не здесь)
                    //_ = _test.ViewReportInConsole(constantsSet);

                    // и ещё не забыть проверить состояние рабочего ключа - там могли скопиться задачи
                    // и для этого тоже нужен тест...


                }
            });
            Logs.Here().Information("Subscription on event key {0} was registered", eventKeyTest);
        }
    }
}
