using System;
using System.Threading;
using System.Threading.Tasks;
using CachingFramework.Redis.Contracts.Providers;
using Microsoft.Extensions.Configuration;
using Shared.Library.Models;
using Shared.Library.Services;

namespace BackgroundDispatcher.Services
{
    public interface ISettingConstantsService
    {
        public void SubscribeOnBaseConstantEvent();
        public Task<ConstantsSet> ConstantInitializer(CancellationToken stoppingToken);
    }

    public class SettingConstantsService : ISettingConstantsService
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
                    Logs.Here().Information("EventKeyNames fetched constants in EventKeyNames - {@D}.", new { CycleDelay = constantsSet.TaskEmulatorDelayTimeInMilliseconds.LifeTime });
                }
                else
                {
                    Logs.Here().Error("eventKeysSet CANNOT be Init.");
                    return null;
                }

                Logs.Here().Information("constantsSet was set, isConstantsSet = {0}.", isConstantsSet);

                string backgroundDispatcherGuid = _guid ?? throw new ArgumentNullException(nameof(_guid));
                constantsSet.BackgroundDispatcherConstant.Guid.Value = backgroundDispatcherGuid;
                Logs.Here().Information("bookTextSplitGuid = {0}", constantsSet.BackgroundDispatcherConstant.Guid.Value);

                // создать именованный гуид сервера из префикса PrefixBookTextSplit и bookTextSplit server Guid
                string backgroundDispatcherPrefixGuid = $"{constantsSet.BackgroundDispatcherConstant.Prefix.Value}:{backgroundDispatcherGuid}";
                constantsSet.BackgroundDispatcherConstant.PrefixGuid.Value = backgroundDispatcherPrefixGuid;
                constantsSet.BackgroundDispatcherConstant.PrefixGuid.LifeTime = constantsSet.BackgroundDispatcherConstant.Prefix.LifeTime;
                Logs.Here().Information("backgroundDispatcherPrefix = {0}, + Guid = {1}", constantsSet.BackgroundDispatcherConstant.Prefix.Value, constantsSet.BackgroundDispatcherConstant.PrefixGuid.Value);

                // test
                Logs.Here().Information("constantsSet = {@C}", new { ConstantsSet = constantsSet });

            }

            return constantsSet;
        }
    }
}
