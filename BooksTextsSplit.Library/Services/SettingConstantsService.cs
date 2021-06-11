﻿using System;
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
            string backServerGuid = _guid ?? throw new ArgumentNullException(nameof(_guid));
            constantsSet.BackServerGuid.Value = backServerGuid;
            string backServerPrefixGuid = $"{constantsSet.PrefixBackServer.Value}:{backServerGuid}";
            constantsSet.BackServerPrefixGuid.Value = backServerPrefixGuid;

            // регистрируем сервер на общем ключе серверов
            await _cache.WriteHashedAsync<string>(constantsSet.EventKeyBackReadiness.Value, backServerPrefixGuid, backServerGuid, constantsSet.EventKeyBackReadiness.LifeTime);

            string prefixProcessAdd = constantsSet.PrefixProcessAdd.Value; // process:add
            string processAddPrefixGuid = $"{prefixProcessAdd}:{backServerGuid}"; // process:add:(this server guid)
            constantsSet.ProcessAddPrefixGuid.Value = processAddPrefixGuid;

            string prefixProcessCancel = constantsSet.PrefixProcessCancel.Value; // process:cancel
            string processCancelPrefixGuid = $"{prefixProcessCancel}:{backServerGuid}"; // process:cancel:(this server guid)
            constantsSet.ProcessCancelPrefixGuid.Value = processCancelPrefixGuid;

            string prefixProcessCount = constantsSet.PrefixProcessCount.Value; // process:count
            string processCountPrefixGuid = $"{prefixProcessCount}:{backServerGuid}"; // process:count:(this server guid)
            constantsSet.ProcessCountPrefixGuid.Value = processCountPrefixGuid;

            // инициализовать поле общего количества процессов при подписке - можно перенести в инициализацию, set "CurrentProcessesCount" in constants
            //await _cache.SetHashedAsync<int>(processAddPrefixGuid, eventFieldBack, 0, TimeSpan.FromDays(eventKeysSet.EventKeyBackReadinessTimeDays));

            Logs.Here().Information("Server Guid was fetched and stored into EventKeyNames. \n {@S}", new { ServerId = backServerPrefixGuid });
            return constantsSet;
        }
    }
}
