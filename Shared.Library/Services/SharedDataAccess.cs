using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CachingFramework.Redis.Contracts;
using CachingFramework.Redis.Contracts.Providers;
using Serilog;
using Shared.Library.Models;

namespace Shared.Library.Services
{
    public interface ISharedDataAccess
    {
        public (string, string, string) FetchBaseConstants([CallerMemberName] string currentMethodNameName = "");
        public Task<ConstantsSet> DeliveryOfUpdatedConstants(CancellationToken cancellationToken);
        public void SubscribeOnBaseConstantEvent();
        public bool IsExistUpdatedConstants();
    }

    public class SharedDataAccess : ISharedDataAccess
    {
        private readonly ICacheManageService _cache;
        //private readonly ICacheProviderAsync _cache;
        private readonly IKeyEventsProvider _keyEvents;

        public SharedDataAccess(
            ICacheManageService cache,
            IKeyEventsProvider keyEvents)
        {
            _cache = cache;
            _keyEvents = keyEvents;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<SharedDataAccess>();

        // эти константы должны быть объявлены локально (только в одном месте), чтобы быть одинаковыми во всех проектах
        // они нужны до появления набора констант, поэтому объявлены здесь для всех серверов
        private const string StartConstantKey = "constants";
        // константы на этом поле нужны для совместимости со старым кодом (хотя его уже наверное не осталось)
        private const string ConstantsStartLegacyField = "constantsBase";
        // рабочий набор констант на поле с номером дата-сервера, поле со старым номером удаляется при рестарте дата-сервера
        private const string ConstantsStartGuidField = "constantsGuidField";
        private const KeyEvent SubscribedKeyEvent = KeyEvent.HashSet;
        //SharedDataAccess cannot find constants and still waits them {TimeToWaitTheConstants} sec more
        private const double TimeToWaitTheConstants = 10;

        private bool _constantsUpdateIsAppeared = false;
        private bool _wasSubscribedOnConstantsUpdate = false;

        // метод только для сервера констант - чтобы он узнал базовые ключ и поле, куда класть текущий ключ констант
        public (string, string, string) FetchBaseConstants([CallerMemberName] string currentMethodNameName = "") // May be will cause problem with Docker
        {
            // if problem with Docker can use token
            const string actualMethodNameWhichCanCallThis = "ConstantsMountingMonitor";
            if (currentMethodNameName != actualMethodNameWhichCanCallThis)
            {
                //_logger.LogError(710070, "FetchBaseConstants was called by wrong method - {0}.", currentMethodNameName);
                Logs.Here().Error("FetchBaseConstants was called by wrong method {@M}.", new { Method = currentMethodNameName });
                return (null, null, null);
            }
            return (StartConstantKey, ConstantsStartLegacyField, ConstantsStartGuidField);
        }

        // этот код - первое, что выполняется на старте - отсюда должны вернуться с константами

        // если констант нет, подписаться и ждать, если подписка здесь, то это общий код
        // можно вернуться без констант, но с ключом для подписки и подписаться в классе подписок - чтобы всё было в одном месте
        // кроме того, это позволит использовать один и тот же универсальный обработчик для всех подписок
        // наверное, разрывать процесс получения констант нехорошо - придётся у всех потребителей повторять подписку в своих классах
        // поэтому подписку оставляем здесь
        // тут должно быть законченное решение не только первоначального получения констант, но и их обновления
        // в подписке ниже поднимем флаг, что надо проверить обновление
        // и когда (если) приложение заглянет сюда проверить константы, запустить получение обновлённого ключа
        // по флагу ничего не проверять, только брать ключ и из него константы
        // сбросить флаг в начале проверки - в цикле while(этот флаг) и если за время проверки подписка опять сработает, то взять константы ещё раз

        // стартовый метод (местный main)
        public async Task<ConstantsSet> DeliveryOfUpdatedConstants(CancellationToken cancellationToken)
        {
            // проверить наличие базового ключа, проверить наличие поля обновлений, можно в одном методе
            // можно в первом проверить - если ключ есть, вернуть - старое поле, если нет нового и новое поле, если оно есть
            // если ключа нет вообще - null
            // следующий шаг - подписаться на ключ в любом варианте или можно подписаться в первую очередь
            // если ключ есть - достать значение поля - это будет или набор или строка
            // если набор - вернуть его и отменить подписку (значит, работает старый вариант констант)
            // если строка, использовать её как ключ (или поле?) и достать обновляемый набор

            // если ещё не подписаны (первый вызов) - подписаться
            // можно получать информацию о первом вызове от вызывающего метода - пусть проверит наличие констант и скажет
            // только накладные будут выше, наверное - зато без лишнего глобального флага

            while (!cancellationToken.IsCancellationRequested)
            {
                // проверить, есть ли ключ вообще
                bool isExistStartConstantKey = await _cache.IsKeyExist(StartConstantKey);

                if (isExistStartConstantKey)
                {
                    // если ключ есть, то есть ли поле обновляемых констант (и в нем поле гуид)
                    string dataServerPrefixGuid = await _cache.FetchHashedAsync<string>(StartConstantKey, ConstantsStartGuidField);

                    if (dataServerPrefixGuid == null)
                    {
                        // обновляемых констант нет в этой версии (или ещё нет), достаём старые и возвращаемся
                        return await _cache.FetchHashedAsync<ConstantsSet>(StartConstantKey, ConstantsStartLegacyField);
                    }

                    if (!_wasSubscribedOnConstantsUpdate)
                    {
                        SubscribeOnGuidConstantsEvent(dataServerPrefixGuid, SubscribedKeyEvent);
                    }

                    // есть обновлённые константы, достаём их, сбрасываем флаг наличия обновления и возвращаемся
                    ConstantsSet constantsSet = await _cache.FetchHashedAsync<ConstantsSet>(dataServerPrefixGuid, ConstantsStartGuidField);
                    _constantsUpdateIsAppeared = false;
                    return constantsSet;
                }

                
                Logs.Here().Warning("SharedDataAccess cannot find constants and still waits them {0} sec more.", TimeToWaitTheConstants);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(TimeToWaitTheConstants), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Prevent throwing if the Delay is cancelled
                }
            }
            // сюда можем попасть только при завершении сервера, константы уже никому не нужны
            return null;
        }

        public bool IsExistUpdatedConstants()
        {
            return _constantsUpdateIsAppeared;
        }
        
        // в этой подписке выставить флаг класса, что надо проверить обновление
        private void SubscribeOnGuidConstantsEvent(string keyGuid, KeyEvent eventToSubscribe)
        {
            _wasSubscribedOnConstantsUpdate = true;
            Logs.Here().Information("SharedDataAccess will be subscribed on keyGuid {0}.", keyGuid);

            //// в константах подписку на ключ сервера сделать в самом начале и сразу проверить наличие констант на этом ключе, если есть, поднять флаг не в самой подписке, а ещё в подписке на подписку
            //if (isExistStartConstantKey)
            //{
            //    _constantsUpdateIsAppeared = true;
            //}

            _keyEvents.Subscribe(keyGuid, (string key, KeyEvent cmd) =>
            {
                if (cmd == eventToSubscribe)
                {
                    Logs.Here().Information("Key {Key} with command {Cmd} was received.", keyGuid, cmd);

                    _constantsUpdateIsAppeared = true;

                    Logs.Here().Information("Constants Update is appeared = {0}.", _constantsUpdateIsAppeared);
                }
            });
            Logs.Here().Information("SharedDataAccess was subscribed on keyGuid {0}.", keyGuid);
        }

        // подписаться в самом начале кода и при смене гуид дата-сервера будет заново выполняться подписка на этот гуид
        public void SubscribeOnBaseConstantEvent()
        {
            Logs.Here().Information("SharedDataAccess will be subscribed on keyGuid {0}.", StartConstantKey);
            
            _keyEvents.Subscribe(StartConstantKey, (string key, KeyEvent cmd) =>
            {
                if (cmd == SubscribedKeyEvent)
                {
                    Logs.Here().Information("Key {Key} with command {Cmd} was received.", StartConstantKey, cmd);

                    // сбрасываем флаг, что подписка уже была произведена и при обновлении заново подпишется на (новый) гуид-ключ
                    _wasSubscribedOnConstantsUpdate = false;
                    // ставим флаг, что было обновление констант и надо проверить гуид-ключ
                    // тогда подписка на старый гуид-ключ сменится на новый
                    _constantsUpdateIsAppeared = true;

                    Logs.Here().Information("Was Subscribed On Constants Update = {0}, Constants Update is appeared = {1}.", _wasSubscribedOnConstantsUpdate, _constantsUpdateIsAppeared);
                }
            });
            Logs.Here().Information("SharedDataAccess was subscribed on keyGuid {0}.", StartConstantKey);
        }
    }

    public static class LoggerExtensions
    {
        // https://stackoverflow.com/questions/29470863/serilog-output-enrich-all-messages-with-methodname-from-which-log-entry-was-ca/46905798

        public static ILogger Here(this ILogger logger, [CallerMemberName] string memberName = "", [CallerLineNumber] int sourceLineNumber = 0)
        //[CallerFilePath] string sourceFilePath = "",
        {
            return logger.ForContext("MemberName", memberName).ForContext("LineNumber", sourceLineNumber);
            //.ForContext("FilePath", sourceFilePath)
        }
    }
}