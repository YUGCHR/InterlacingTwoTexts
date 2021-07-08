using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using CachingFramework.Redis.Contracts;
using CachingFramework.Redis.Contracts.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Library.Models;
using Shared.Library.Services;
using BackgroundDispatcher.Services;

namespace BackgroundDispatcher
{
    public class MonitorLoop
    {
        private readonly CancellationToken _cancellationToken;
        private readonly ISettingConstantsS _constants;
        private readonly ISharedDataAccess _data;
        private readonly ICacheManageService _cache;
        private readonly IOnKeysEventsSubscribeService _subscribe;

        public MonitorLoop(
            IHostApplicationLifetime applicationLifetime,
            ISettingConstantsS constants,
            ISharedDataAccess data,
            ICacheManageService cache,
            IOnKeysEventsSubscribeService subscribe)
        {
            _cancellationToken = applicationLifetime.ApplicationStopping;
            _constants = constants;
            _cache = cache;
            _data = data;
            _subscribe = subscribe;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<MonitorLoop>();

        public void StartMonitorLoop()
        {
            Logs.Here().Information("Monitor Loop is started.");

            // Run a console user input loop in a background thread
            Task.Run(Monitor, _cancellationToken);
        }

        // задачи монитора (Monitor) -
        // 1. получить константы
        // дальнейшие обновления констант будут проверяться перед запуском задачи
        // 2. проверить окружение
        // окружение - достаточное количество бэк-серверов
        // 3. вызвать метод план подписок
        // после регистрации подписок, сервер перейдёт в решим ожидания команд от контроллера
        // 4. обработать завершение работы
        // при завершении надо закрыть лог и развеять таймер, может, ещё что

        public async Task Monitor() // _logger = 100
        {
            // диспетчер считает вызовы подписки и секунды
            // как что-то наберётся, забирает накопленное и стирает поля
            // то, что получилось стереть, идёт в работу - на формирование пакета задач
            //
            // 1 - диспетчер - формирование пакетов
            // 2 - бэк - сервер - запись в кэш
            // 3 - проверка версии в кэше
            // 4 - чтение глав
            //
            // диспетчер проверяет запись отданных пакетов и затем отдаёт команду на синхронизацию с базой или даже по bookId, нет, ещё надо по версиям и языкам
            // инспектор базы данных - удаляет произвольный ключ книги и смотрит, через сколько времени его восстановят

            // use guid
            ConstantsSet constantsSet = await _constants.ConstantInitializer(_cancellationToken);
            // можно метод проверки загрузки констант вынести в общую библиотеку и проверять полученные константы из ключа во всех серверах (правда, непонятно зачем)

            // на старте проверить наличие ключа с константами
            // в сервисе констант при старте удалять ключ и создавать новый
            // константы можно делить по полям, а можно общим классом
            // а можно и то и другое
            // если все константы классом, то получится надо везде держать модель - одинаковый код
            // на старте фронт сразу запускает два (взять из constant) бэка - чтобы были
            int serverCount = 1;
            // пока запустить руками, потом в контейнерах - вроде как не получится

            // тут можно проверить наличие минимум двух бэк-серверов
            // а можно перенести в цикл ожидания нажатия клавиши

            // здесь можно подписаться на ключ регистрации серверов и при оповещении по подписке, обновлять лист серверов и проверять, что все живые
            // ещё менять флаг разрешения задач
            // это уже функции будущего диспетчера

            IDictionary<string, string> tasksList = await _cache.FetchHashedAllAsync<string>(constantsSet.EventKeyBackReadiness.Value);
            int tasksListCount = tasksList.Count;
            if (tasksListCount < serverCount)
            {
                // если серверов меньше заданного минимума, сидеть здесь и ждать регистрации нужного количества
                //_logger.LogInformation(1001, "Please, start {0} instances of BackgroundTasksQueue server", serverCount);
                Logs.Here().Warning("Please, start {0} instances of BackgroundTasksQueue server.", serverCount);
                // или если есть хотя бы один, то не ждать, а работать?
                // ждать - значит поставить флаг разрешения размещения задач в запрещено, подписка сама оповестит, что произошли изменения
            }

            // при старте, если констант вдруг нет, можно подписаться на стандартный общий ключ получения констант
            // когда константы создадутся, они оповестят по подписке, что константы уже есть и тогда по другому стандартному ключу их взять
            // например, constants:fetch - забирать, constants:await - подписка на ожидание
            // в контейнерах будет непросто разбираться, в каком порядке все должны стартовать, редис первым - уже достижение
            // ещё регистрация сервера в ключе может быть, а сам сервер уже (давно) неживой
            // поэтому в значение поля с номером сервера можно класть не тот же самый номер, а ключ для проверки живости сервера, на который тот будет подписан
            // а лучше просто чистый гуид сервера - они к нему применят разные префиксы для подписки из общих констант
            // скажем, сервер будет подписан на динг:гуид, а фронт, получив гуид сервера, подпишется на донг:гуид и сделает запись на динг
            // после этого сервер, увидев запрос, запишет в ключ донг:гуид и фронт узнает, что сервер живой и общается
            // после чего оба ключа надо сразу же удалить

            // добавить дополнительный ключ с количеством пакетов задач
            // в стартовом ключе в значении указывать задержку -
            // положительная - в секундах,
            // 0 - без задержки,
            // отрицательная - случайная задержка, но не более значения в сек

            // generate random integers from 5 to 10
            Random rand = new();
            rand.Next(5, 11);

            // тут необходимо очистить ключ EventKeyFrontGivesTask, может быть временно, для отладки
            string eventKeyFrontGivesTask = constantsSet.EventKeyFrontGivesTask.Value;
            Logs.Here().Information("Key eventKeyFrontGivesTask = {0} fetched from constants.", eventKeyFrontGivesTask);

            // можно не проверять наличие ключа, а сразу пробовать удалить, там внутри есть своя проверка
            bool isExistEventKeyFrontGivesTask = await _cache.IsKeyExist(eventKeyFrontGivesTask);
            if (isExistEventKeyFrontGivesTask)
            {
                bool isDeleteSuccess = await _cache.DelKeyAsync(eventKeyFrontGivesTask);
                Logs.Here().Information("FrontServerEmulation reported - isDeleteSuccess of the key {0} is {1}.", eventKeyFrontGivesTask, isDeleteSuccess);
            }

            // новая версия, теперь это только эмулятор контроллера фронт-сервера
            // новая версия, теперь это диспетчер задач, получаемых от контроллера и передаваемых бэк-серверам

            // множественные контроллеры по каждому запросу (пользователей) создают очередь - каждый создаёт ключ, на который у back-servers подписка, в нём поле со своим номером, а в значении или имя ключа с заданием или само задание            
            // дальше бэк-сервера сами разбирают задания
            // эмулятор сейчас выполняет старт многих пакетов (задаётся пользователем при старте), в работе от одного контроллера будет один пакет задач
            // все бэк-сервера подписаны на базовый ключ и получив сообщение по подписке, стараются взять задание - у кого получилось удалить ключ, тот и взял
            // у контроллера остаётся базовый ключ, который он вернёт пользователю и тот потом сможет контролировать ход выполнения задания
            // тогда лишняя сущность диспетчера не нужна, но если задание упадёт, восстановить его будет некому

            // второй вариант - диспетчер собирает всё задания (от множества контроллеров) и ставит у себя в очередь, потом берёт по очереди бэк-сервера и выдаёт им задания
            // named it controllers-dispatcher-queue-back-servers
            // тогда диспетчер по подписке на ключ сервера знает о ходе выполнения и если сообщения прекратятся, но ещё не 100%, сможет перезапустить задачу
            // можно усреднять время выполнения каждого этапа задания и хранить предполагаемое полное время выполнения, а по его истечению принимать какие-то меры

            // первый вариант позволяет его потом дополнить надстройкой диспетчера, которому надо будет следить только за целостностью бэк-серверов и давать команду на восстановление ключа задачи (и, возможно, удаление зависшего сервера)

            // план работы эмулятора
            // ждёт команды с консоли с количеством генерируемых пакетов
            // по получению начинает цикл создания пакетов с задачами
            // первый гуид - главный номер задания, второй - ключ доступа к заданию (или один и тот же номер)
            // при создании пакета сначала создаётся пакет задач в ключе, а потом этот номер создаётся в виде поля в подписном ключе
            // собственно, это пока всё (потом можно добавить случайную задержку между генерацией отдельных пакетов)




            _subscribe.SubscribingPlan(constantsSet);


            // сделать обработку завершения работы и развеять таймер

            while (IsCancellationNotYet)
            {
                var keyStroke = Console.ReadKey();

                if (keyStroke.Key == ConsoleKey.W)
                {
                    Logs.Here().Information("ConsoleKey was received {KeyStroke}.\n", keyStroke.Key);
                }
            }
        }

        private bool IsCancellationNotYet => !_cancellationToken.IsCancellationRequested; // add special key from Redis?
    }
}

