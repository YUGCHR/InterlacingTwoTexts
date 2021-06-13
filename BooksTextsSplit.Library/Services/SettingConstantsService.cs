using System;
using System.Threading;
using System.Threading.Tasks;
using CachingFramework.Redis.Contracts.Providers;
using Microsoft.Extensions.Configuration;
using Shared.Library.Models;
using Shared.Library.Services;

namespace BooksTextsSplit.Library.Services
{
    public interface ISettingConstantsS
    {
        public bool IsExistUpdatedConstants();
        public void SubscribeOnBaseConstantEvent();
        public Task<ConstantsSet> ConstantInitializer(CancellationToken stoppingToken);
    }

    public class SettingConstantsService : ISettingConstantsS
    {
        private readonly ICacheManageService _cache;
        private readonly ISharedDataAccess _data;
        private readonly string _guid;

        public SettingConstantsService(
            GenerateThisInstanceGuidService thisGuid,
            ISharedDataAccess data,
            ICacheManageService cache)
        {
            _data = data;
            _cache = cache;
            _guid = thisGuid.ThisBackServerGuid();
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<SettingConstantsService>();

        public bool IsExistUpdatedConstants()
        {
            return _data.IsExistUpdatedConstants();
        }

        public void SubscribeOnBaseConstantEvent()
        {
            _data.SubscribeOnBaseConstantEvent();
        }

        // инициализация констант для BookTextSplit
        public async Task<ConstantsSet> ConstantInitializer(CancellationToken stoppingToken)
        {
            // сюда попадаем перед каждым пакетом, основные варианты
            // 1. старт сервера, первоначальное получение констант
            // 2. старт сервера, нет базового ключа констант
            // 3. старт сервера, есть базовый ключ, но нет ключа обновления констант
            // 4. новый пакет, нет обновления
            // 5. новый пакет, есть обновление
            // 6. новый пакет, пропал ключ обновления констант
            // 7. новый пакет, пропал базовый ключ констант

            ConstantsSet constantsSet = await _data.DeliveryOfUpdatedConstants(stoppingToken);

            // здесь уже с константами
            if (constantsSet != null)
            {
                Logs.Here().Debug("EventKeyNames fetched constants in EventKeyNames - {@D}.", new { CycleDelay = constantsSet.TaskEmulatorDelayTimeInMilliseconds.LifeTime });
            }
            else
            {
                Logs.Here().Error("eventKeysSet CANNOT be Init.");
                return null;
            }

            

            
            
            // передать время ключа во все созданные константы backServer из префикса PrefixBackServer
            string bookTextSplitGuid = _guid ?? throw new ArgumentNullException(nameof(_guid));
            constantsSet.BookTextSplitGuid.Value = bookTextSplitGuid;

            // создать именованный гуид сервера из префикса PrefixBookTextSplit и bookTextSplit server Guid
            string bookTextSplitPrefixGuid = $"{constantsSet.PrefixBookTextSplit.Value}:{bookTextSplitGuid}";
            constantsSet.BookTextSplitPrefixGuid.Value = bookTextSplitPrefixGuid;

            // создать ключ для хранения плоского текста книги из префикса BookTextFieldPrefix и bookTextSplit server Guid 
            string bookPlainTextKeyPrefixGuid = $"{constantsSet.BookPlainTextKeyPrefix.Value}:{bookTextSplitGuid}";
            constantsSet.BookPlainTextKeyPrefixGuid.Value = bookPlainTextKeyPrefixGuid;

            // создать поле для хранения плоского текста книги из префикса BookTextFieldPrefix и - нет, его создавать локально





            Logs.Here().Information("BookTextSplit Server Guid was fetched and stored into EventKeyNames. \n {@S}", new { ServerId = bookTextSplitPrefixGuid });
            return constantsSet;
        }
    }
}
