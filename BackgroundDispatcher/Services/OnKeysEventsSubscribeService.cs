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
        public void SubscribingPlan(ConstantsSet constantsSet);
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

        public void SubscribingPlan(ConstantsSet constantsSet)
        {
            string eventKeyFrom = constantsSet.EventKeyFrom.Value; // subscribeOnFrom
            KeyEvent eventCmd = constantsSet.EventCmd; // HashSet            

            string eventKeyTest = constantsSet.Prefix.IntegrationTestPrefix.KeyStartTestEvent.Value; // test
            string eventKeyFromTest = $"{eventKeyFrom}:{eventKeyTest}";

            _count.EventCounterInit(constantsSet);

            // по умолчанию ставим переключатель Test/Work в положение работа
            _isTestInProgress = false;

            // подписка на ключ создания задачи (загрузки книги)
            SubscribeOnEventFrom(constantsSet, eventKeyFrom, eventCmd);

            // подписка на фальшивый (тестовый) ключ создания задачи
            SubscribeOnEventFrom(constantsSet, eventKeyFromTest, eventCmd);

            // подписка на ключ для старта тестов
            SubscribeOnTestEvent(constantsSet, eventKeyTest, eventCmd);

            char separatorUnit = '-';
            string messageText = "To start Test please type from Redis console the following command - ";
            string testConsoleCommand = $"127.0.0.1:637 > hset {eventKeyTest} test 1";
            (string frameSeparator1, string inFrameTextMessage) = GenerateMessageInFrame.CreateMeassageInFrame(separatorUnit, testConsoleCommand);
            Logs.Here().Information("To start Test please type from Redis console the following command - \n {0} \n {1} \n {2}", frameSeparator1, inFrameTextMessage, frameSeparator1);
        }

        // bool isTestNotInProgress = !_isTestInProgress;
        private void SubscribeOnEventFrom(ConstantsSet constantsSet, string eventKey, KeyEvent eventCmd)
        {
            _keyEvents.Subscribe(eventKey, (key, cmd) => // async
            {
                if (cmd == eventCmd)
                {
                    _ = _count.EventCounterOccurred(constantsSet, eventKey, _cancellationToken);
                }
            });

            Logs.Here().Information("Subscription on event key {0} was registered", eventKey);
        }

        // подписка на команду на запуск тестов
        private void SubscribeOnTestEvent(ConstantsSet constantsSet, string eventKey, KeyEvent eventCmd)
        {
            _keyEvents.Subscribe(eventKey, async (key, cmd) =>
            {
                if (cmd == eventCmd)
                {
                    // ещё проверить счётчик и если не нулевой, ждать обнуления
                    // за ним придётся ходить в следующий класс
                    //

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

            Logs.Here().Information("Subscription on event key {0} was registered", eventKey);
        }

    }
}
