using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using ConstantData.Services;
using Shared.Library.Models;
using Shared.Library.Services;
using Newtonsoft.Json;

namespace ConstantData
{
    public class MonitorLoop
    {
        private readonly ISharedDataAccess _data;
        private readonly IOnKeysEventsSubscribeService _subscribe;
        private readonly IConstantsCollectionService _collection;
        private readonly ICacheManagerService _cache;
        private readonly IAuxiliaryUtilsService _aux;
        private readonly CancellationToken _cancellationToken;
        private readonly string _guid;

        public MonitorLoop(
            ISharedDataAccess data,
            IOnKeysEventsSubscribeService subscribe,
            IConstantsCollectionService collection,
            ICacheManagerService cache,
            IAuxiliaryUtilsService aux,
            IHostApplicationLifetime applicationLifetime,
            GenerateThisInstanceGuidService thisGuid)
        {
            _data = data;
            _subscribe = subscribe;
            _collection = collection;
            _cache = cache;
            _aux = aux;
            _cancellationToken = applicationLifetime.ApplicationStopping;
            _guid = thisGuid.ThisBackServerGuid();
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<MonitorLoop>();

        public void StartMonitorLoop()
        {
            Logs.Here().Information("ConstantsMountingMonitor Loop is starting.");

            Task.Run(ConstantsMountingMonitor, _cancellationToken);
        }

        public async Task ConstantsMountingMonitor()
        {
            // тут заехали все константы из GetSection("SettingConstants").Bind(SettingConstants)
            ConstantsSet constantsSet = _collection.SettingConstants;            
            bool constantsSetIsHealthy0 = AuxiliaryUtilsService.CheckConstantSet(constantsSet);
            
            // выгружаем файл appsetting.json в текстовый массив
            string dataFileName = "appsettings-Copy.json"; // @"D:\ActiveSolutions\ConstantData\appsetting.json";
            (bool constantsSetIsHealthy1, List<string> appsettingLines) = ConvertConstantsFileToList(dataFileName);

            // получение стартовых ключей, общих для всех, чтобы найти гуид-ключ с константами
            (string startConstantKey, string constantsStartLegacyField, string constantsStartGuidField) = _data.FetchBaseConstants();

            string dataServerPrefixGuid = $"{constantsSet.PrefixDataServer.Value}:{_guid}";
            double baseLifeTime = constantsSet.PrefixDataServer.LifeTime;
            // записываем в строку версии констант основной гуид-ключ
            constantsSet.ConstantsVersionGuidKey.Value = dataServerPrefixGuid;
            constantsSet.ConstantsVersionGuidKey.LifeTime = baseLifeTime;
            constantsSet.ConstantsVersionBaseKey.Value = startConstantKey;
            constantsSet.ConstantsVersionBaseKey.LifeTime = baseLifeTime;
            constantsSet.ConstantsVersionBaseField.Value = constantsStartGuidField;
            constantsSet.ConstantsStartLegacyField.Value = constantsStartLegacyField;

            // записываем константы в стартовый ключ и старое поле (для совместимости)
            await _cache.SetStartConstants(constantsSet.ConstantsVersionBaseKey.Value, constantsStartLegacyField, constantsSet, constantsSet.ConstantsVersionBaseKey.LifeTime);
            //Logs.Here().Information("ConstantData sent constants to {@K} / {@F}.", new { Key = constantsSet.ConstantsVersionBaseKey.Value }, new { Field = constantsStartLegacyField });


            // сервер констант имеет свой гуид и это ключ обновляемых констант
            // его он пишет в поле для нового гуид-ключа для всех
            // на этот ключ уже можно подписаться, он стабильный на всё время существования сервера
            // если этот ключ исчезнет(сервер перезапустился), то надо перейти на базовый ключ и искать там
            // на этом ключе будут сменяемые поля с константами - новое появилась, старое удалили
            // тогда будет смысл в подписке
            // в подписке всё равно мало смысла, даже если есть известие от подписки, надо проверять наличие гуид-ключа -
            // может же сервер исчезнуть к этому времени, забрав с собой ключ
            // можно ключ не удалять, даже нужно - если сервер упадёт неожиданно, то ключи всё равно останутся
            // но ключ может и исчезнуть сам по себе, надо проверять
            // наверное, подписка имеет смысл для мгновенной реакции или для длительного ожидания
            // если сервер простаивает, то обновления констант ему всё равно не нужны
            // если, конечно, не обновятся какие-то базовые ключи, но это допускать нельзя
            // можно разделить набор на два - изменяемый и постоянный
            // постоянные инициализовать через инит, а остальные добавлять по ходу - по ключам изменения
            // поэтому сервер получит новые константы после захвата пакета


            // проверяем наличие старого ключа гуид-констант и если он есть, удаляем его
            //Logs.Here().Information("ConstantData check old Guid in key {0} / field {1}.", constantsSet.ConstantsVersionBaseKey.Value, constantsStartGuidField);
            string oldGuidConstants = await _cache.FetchHashedAsync<string>(constantsSet.ConstantsVersionBaseKey.Value, constantsStartGuidField);
            if (oldGuidConstants != null)
            {
                bool oldGuidConstantsWasDeleted = await _cache.DelKeyAsync(oldGuidConstants);
                Logs.Here().Information("Old Constants {0} was deleted - {1}.", oldGuidConstants, oldGuidConstantsWasDeleted);
            }

            // записываем в стартовый ключ и новое поле гуид-ключ обновляемых констант
            await _cache.SetConstantsStartGuidKey(constantsSet.ConstantsVersionBaseKey, constantsStartGuidField, dataServerPrefixGuid);
            //Logs.Here().Information("ConstantData set {@G} to {@K} / {@F}.", new { Guid = dataServerPrefixGuid }, new { Key = constantsSet.ConstantsVersionBaseKey.Value }, new { Field = constantsStartGuidField });

            // записываем константы в новый гуид-ключ и новое поле (надо какое-то всем известное поле)
            // потом может быть будет поле-версия, а может будет меняться ключ

            // передавать переменную класса с временем жизни вместо строки
            await _cache.SetStartConstants(constantsSet.ConstantsVersionGuidKey.Value, constantsSet.ConstantsVersionBaseField.Value, constantsSet, constantsSet.ConstantsVersionGuidKey.LifeTime);
            //Logs.Here().Information("ConstantData sent constants to {@K} / {@F}.", new { Key = constantsSet.ConstantsVersionBaseKey.Value }, new { Field = constantsStartGuidField });

            bool constantsSetIsHealthy2 = AuxiliaryUtilsService.CheckConstantSet(constantsSet);

            //Logs.Here().Information("SConstants set healthy is {0}, {1}, {2}.", constantsSetIsHealthy0, constantsSetIsHealthy1, constantsSetIsHealthy2);
            bool neverthelessWentWrong = _aux.SomethingWentWrong(constantsSetIsHealthy0, constantsSetIsHealthy1, constantsSetIsHealthy2);

            if (neverthelessWentWrong)
            {
                Logs.Here().Error("ConstantsSet is {0}.", neverthelessWentWrong);
            }
            else
            {
                // подписываемся на ключ сообщения о необходимости обновления констант, отдаем ему текстовый список констант appsettingLines
                _subscribe.SubscribeOnEventUpdate(constantsSet, constantsStartGuidField, appsettingLines);
                //Logs.Here().Information("SettingConstants ConstantsVersionBase = {0}, ConstantsVersionNumber = {1}.", constantsSet.ConstantsVersionBaseKey.Value, constantsSet.ConstantsVersionNumber.Value);
            }

            while (true)
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    bool res = await _cache.DeleteKeyIfCancelled(startConstantKey);
                    Logs.Here().Warning("Cancellation Token was received, key was removed = {KeyStroke}.", res);

                    return;
                }

                var keyStroke = Console.ReadKey();

                if (keyStroke.Key == ConsoleKey.W)
                {
                    Logs.Here().Information("ConsoleKey was received {KeyStroke}.", keyStroke.Key);
                }

                await Task.Delay(10, _cancellationToken);
            }
        }

        public static (bool, List<string>) ConvertConstantsFileToList(string dataFileName)
        {
            StreamReader file = new StreamReader(@dataFileName);
            // можно хранить Capacity в последней строке appsetting
            int appsettingLinesCapacity = 400;
            List<string> appsettingLines = new(appsettingLinesCapacity);
            int lineCounter = 0;
            string line;
            Logs.Here().Information("Check dataFileName = {0}, appsettingLinesCapacity = {1}, lineCounter = {2}.", dataFileName, appsettingLinesCapacity, lineCounter);

            while ((line = file.ReadLine()) != null)
            {
                appsettingLines.Add(line);
                lineCounter++;
            }
            file.Close();

            //Logs.Here().Information("{@A}", new{List = appsettingLines});
            //int appsettingLinesCountWrite = appsettingLines.Count;
            //for(int i = 0; i< appsettingLinesCountWrite - 1; i++)
            //{
            //    Console.WriteLine($"{appsettingLines[i]}");
            //}

            // удаляем первую (не нулевую) - / "SettingConstants": { / и последнюю (образовалась лишняя } ) - (последнюю не удаляем )строки файла
            int strNum = 1;
            Logs.Here().Information("{0} strings was read from file {1} and string[{2}] = {3} will be removed.", lineCounter, dataFileName, 1, appsettingLines[strNum]);
            appsettingLines.RemoveAt(strNum);

            //int appsettingLinesCount0 = appsettingLines.Count;
            //Logs.Here().Information("appsettingLines last - {0} - string {1} will be removed.", appsettingLinesCount0 - 1, appsettingLines[appsettingLinesCount0 - 1]);
            //appsettingLines.RemoveAt(appsettingLinesCount0 - 1);

            int appsettingLinesCount1 = appsettingLines.Count;
            // текстовый список констант appsettingLines готов к синтаксическому анализу
            Logs.Here().Information("appsettingLines with {0} strings is ready to parse.", appsettingLinesCount1);

            // тестовая сборка в класс
            string constantSetFromText = String.Join(' ', appsettingLines.ToArray(), 0, appsettingLines.Count - 1);
            //Console.WriteLine($"\n{constantSetFromText}\n");
            ConstantsSet constantsSetUpdated = new ConstantsSet();
            constantsSetUpdated = JsonConvert.DeserializeObject<ConstantsSet>($"{constantSetFromText}");
            //Logs.Here().Information("\n {@C} \n", new { ConstantsSetUpdated = constantsSetUpdated });
            bool successfulListCreation = AuxiliaryUtilsService.CheckConstantSet(constantsSetUpdated);
            Logs.Here().Information("Constants health check result is {0}.", successfulListCreation);

            return (successfulListCreation, appsettingLines);
        }


        // --- LEGACY --- LEGACY --- LEGACY --- LEGACY --- LEGACY --- LEGACY --- LEGACY --- LEGACY --- LEGACY --- LEGACY --- LEGACY --- LEGACY --- LEGACY --- 

        private static bool ConstantsLoadingSelfTest(ConstantsSet constantsSet)
        {
            if (constantsSet.ConstantsLoadingSelfTestEnd == null || constantsSet.ConstantsLoadingSelfTestBegin == null)
            {
                return false;
            }

            string selfTestSource = constantsSet.ConstantsLoadingSelfTestBegin.Value;
            string selfTestControl = constantsSet.ConstantsLoadingSelfTestEnd.Value;

            if (selfTestControl == selfTestSource)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
