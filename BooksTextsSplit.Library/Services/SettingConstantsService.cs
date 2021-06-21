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
        public void SubscribeOnBaseConstantEvent();
        public Task<ConstantsSet> ConstantInitializer(CancellationToken stoppingToken);
    }

    public class SettingConstantsService : ISettingConstantsS
    {
        private readonly ISharedDataAccess _data;
        private readonly string _guid;

        public SettingConstantsService(
            GenerateThisInstanceGuidService thisGuid,
            ISharedDataAccess data)
        {
            _data = data;
            _guid = thisGuid.ThisBackServerGuid();
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<SettingConstantsService>();

        private ConstantsSet constantsSet = new();
        private bool isConstantsSet = false;

        private bool IsExistUpdatedConstants()
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
            Logs.Here().Information("ConstantInitializer started, isConstantsSet = {0}.", isConstantsSet);

            if (IsExistUpdatedConstants())
            {
                isConstantsSet = false;
            }
            Logs.Here().Information("IsExistUpdatedConstants was chacked, isConstantsSet = {0}.", isConstantsSet);

            if (!isConstantsSet)
            {
                constantsSet = await _data.DeliveryOfUpdatedConstants(stoppingToken);

                // здесь уже с константами
                if (constantsSet != null)
                {
                    isConstantsSet = true;
                    Logs.Here().Debug("EventKeyNames fetched constants in EventKeyNames - {@D}.", new { CycleDelay = constantsSet.TaskEmulatorDelayTimeInMilliseconds.LifeTime });
                }
                else
                {
                    Logs.Here().Error("eventKeysSet CANNOT be Init.");
                    return null;
                }

                Logs.Here().Information("constantsSet was set, isConstantsSet = {0}.", isConstantsSet);

                string bookTextSplitGuid = _guid ?? throw new ArgumentNullException(nameof(_guid));
                constantsSet.BookTextSplit.Guid.Value = bookTextSplitGuid;
                Logs.Here().Information("bookTextSplitGuid = {@G}", constantsSet.BookTextSplit.Guid);

                // создать именованный гуид сервера из префикса PrefixBookTextSplit и bookTextSplit server Guid
                string bookTextSplit_PrefixGuid = $"{constantsSet.BookTextSplit.Prefix.Value}:{bookTextSplitGuid}";
                constantsSet.BookTextSplit.PrefixGuid.Value = bookTextSplit_PrefixGuid;
                constantsSet.BookTextSplit.PrefixGuid.LifeTime = constantsSet.BookTextSplit.Prefix.LifeTime;
                Logs.Here().Information("bookTextSplitPrefix = {@P}, + Guid = {@G}", constantsSet.BookTextSplit.Prefix, constantsSet.BookTextSplit.PrefixGuid);

                // создать ключ для хранения плоского текста книги из префикса BookTextFieldPrefix и bookTextSplit server Guid 
                string bookPlainText_KeyPrefixGuid = $"{constantsSet.BookPlainText.KeyPrefix.Value}:{bookTextSplitGuid}";
                constantsSet.BookPlainText.KeyPrefixGuid.Value = bookPlainText_KeyPrefixGuid;
                constantsSet.BookPlainText.KeyPrefixGuid.LifeTime = constantsSet.BookPlainText.KeyPrefix.LifeTime;
                Logs.Here().Information("bookPlainTextKeyPrefix = {@P}, + Guid = {@G}", constantsSet.BookPlainText.KeyPrefix, constantsSet.BookPlainText.KeyPrefixGuid);

                // создать поле для хранения плоского текста книги из префикса BookTextFieldPrefix и - нет, его создавать локально

            }

            return constantsSet;
        }
    }
}
