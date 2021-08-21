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
        private readonly IIntegrationTestService _test;
        private readonly IEventCounterHandler _count;

        public OnKeysEventsSubscribeService(
            IHostApplicationLifetime applicationLifetime,
            IKeyEventsProvider keyEvents,
            IIntegrationTestService test,
            IEventCounterHandler count
            )
        {
            _cancellationToken = applicationLifetime.ApplicationStopping;
            _keyEvents = keyEvents;
            _test = test;
            _count = count;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<OnKeysEventsSubscribeService>();

        private bool _isTestInProgressAlready;

        public async Task SubscribingPlan(ConstantsSet constantsSet)
        {
            string eventKeyFrom = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
            KeyEvent eventCmd = constantsSet.EventCmd; // HashSet            

            string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            
            // временное удаление рабочих ключей для тестирования (а может и постоянное)
            bool eventKeyFromWasDeleted = await _test.RemoveWorkKeyOnStart(eventKeyFrom);
            bool cafeKeyWasDeleted = await _test.RemoveWorkKeyOnStart(cafeKey);
            Logs.Here().Information("Keys {0} and {1} were deleted successfully - {2} / {3}.", eventKeyFrom, cafeKey, eventKeyFromWasDeleted, cafeKeyWasDeleted);

            // инициализируем поля и таймер в классе EventCounterHandler
            _count.EventCounterInit(constantsSet);

            // ключ блокировки повторного запуска теста до окончания уже запущенного
            _isTestInProgressAlready = false;

            // надо сходить в тесты и инициализировать там местное поле _isTestInProgress
            // потому что в методе TaskPackageFormationFromPlainText.HandlerCallingDistributore это поле проверяется, чтобы определить тест сейчас или реальная работа
            // а если тесты ни разу не вызывались, это поле может быть не определено            
            _test.SetIsTestInProgress(false);

            // подписка на ключ создания задачи (загрузки книги)
            SubscribeOnEventFrom(constantsSet, eventKeyFrom, eventCmd);

            // подписка на ключ для старта тестов
            SubscribeOnTestEvent(constantsSet, eventKeyTest, eventCmd);

            char separatorUnit = '-';
            //string messageText = "To start Test please type from Redis console the following command - ";
            string testConsoleCommand = $"127.0.0.1:6379> hset {eventKeyTest} test 1";
            (string frameSeparator1, string inFrameTextMessage) = GenerateMessageInFrame.CreateMeassageInFrame(separatorUnit, testConsoleCommand);
            Logs.Here().Information("To start Test please type from Redis console the following command - \n {0} \n {1} \n {2}", frameSeparator1, inFrameTextMessage, frameSeparator1);
        }

        // bool isTestNotInProgress = !_isTestInProgress;
        // неудачная идея объединения в один метод, надо обратно разделить на настоящий и фальшивый
        // и настоящий блокировать при старте теста
        // где находится гуид запроса контроллера?
        // надо его обрабатывать и давать контроллеру подтверждение получения задания

        // -----------------------------------------------------------------------------------------------------------------------------------------
        // при обработке плоского текста посчитать его хэш и сохранить в версии книги и потом проверять
        // если это та же самая книга, можно не сохранять
        // очень полезно в плане перемещения мозгов из фронта в бэк - определять версию на сервере и потом показывать пользователю для согласования
        // -----------------------------------------------------------------------------------------------------------------------------------------

        // подписка на ключ создания задачи (загрузки книги)
        private void SubscribeOnEventFrom(ConstantsSet constantsSet, string eventKeyFrom, KeyEvent eventCmd)
        {
            _keyEvents.Subscribe(eventKeyFrom, (key, cmd) => // async
            {
                // сразу после успешного старта тестов блокируется подписка на новые задачи
                // если блокировка всё равно не будет успевать, надо ходить за флагом в класс EventCounterHandler
                // больше тут не надо блокировать
                // после появления ключа запуска теста, контроллер не сможет прислать новое задание
                //bool isTestStarted = _count.IsTestStarted();
                if (cmd == eventCmd) // && !isTestStarted)
                {
                    _ = _count.EventCounterOccurred(constantsSet, eventKeyFrom, _cancellationToken);
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

                    eventCafeIsNotExisted = await _test.EventCafeOccurred(constantsSet, _cancellationToken);

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

                    Logs.Here().Information("Is test in progress state = {0}, integration test started.", _isTestInProgressAlready);
                    // после окончания теста снять блокировку
                    _isTestInProgressAlready = await _test.IntegrationTestStart(constantsSet, _cancellationToken);
                    Logs.Here().Information("Is test in progress state = {0}, integration test finished.", _isTestInProgressAlready);

                    _keyEvents.Unsubscribe(cafeKey);


                    // и ещё не забыть проверить состояние рабочего ключа - там могли скопиться задачи
                    // и для этого тоже нужен тест...


                }
            });

            Logs.Here().Information("Subscription on event key {0} was registered", eventKeyTest);
        }
    }
}
