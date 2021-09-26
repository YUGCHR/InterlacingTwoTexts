using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using CachingFramework.Redis.Contracts;
using CachingFramework.Redis.Contracts.Providers;
using Shared.Library.Models;
using Shared.Library.Services;
using Newtonsoft.Json;

namespace ConstantData.Services
{
    public interface IOnKeysEventsSubscribeService
    {
        public void SubscribeOnEventUpdate(ConstantsSet constantsSet, string constantsStartGuidField, List<string> appsettingLines);
    }

    public class OnKeysEventsSubscribeService : IOnKeysEventsSubscribeService
    {
        private readonly CancellationToken _cancellationToken;
        private readonly ICacheManagerService _cache;
        private readonly IKeyEventsProvider _keyEvents;
        private readonly IAuxiliaryUtilsService _aux;

        public OnKeysEventsSubscribeService(
            IHostApplicationLifetime applicationLifetime,
            IKeyEventsProvider keyEvents,
            ICacheManagerService cache,
            IAuxiliaryUtilsService aux)
        {
            _cancellationToken = applicationLifetime.ApplicationStopping;
            _keyEvents = keyEvents;
            _cache = cache;
            _aux = aux;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<OnKeysEventsSubscribeService>();

        // подписываемся на ключ сообщения о появлении обновления констант
        public void SubscribeOnEventUpdate(ConstantsSet constantsSet, string constantsStartGuidField, List<string> appsettingLines)
        {
            string eventKeyUpdateConstants = constantsSet.EventKeyUpdateConstants.Value;

            Logs.Here().Information("ConstantsData subscribed on EventKey. \n {@E}", new { EventKey = eventKeyUpdateConstants });
            Logs.Here().Information("Constants version is {0}:{1}.", constantsSet.ConstantsVersionBaseKey.Value, constantsSet.ConstantsVersionNumber.Value);

            bool flagToBlockEventUpdate = true;

            _keyEvents.Subscribe(eventKeyUpdateConstants, async (string key, KeyEvent cmd) =>
            {
                if (cmd == constantsSet.EventCmd && flagToBlockEventUpdate)
                {
                    flagToBlockEventUpdate = false;
                    //Logs.Here().Information("eventKeyUpdateConstants {0} was happened, EventUpdateHandler will be called, next call = {1}", eventKeyUpdateConstants, flagToBlockEventUpdate);
                    flagToBlockEventUpdate = await EventUpdateHandler(constantsSet, constantsStartGuidField, appsettingLines);
                }
            });
        }

        private async Task<bool> EventUpdateHandler(ConstantsSet constantsSet, string constantsStartGuidField, List<string> appsettingLines)
        {
            // проверять (в вебе), что константы может обновлять только админ
            // добавить разрешённый диапазон для изменения констант (где его хранить?)

            
            // ключ, где брать обновления констант (и из веба тоже там будут)
            string eventKeyUpdateConstants = constantsSet.EventKeyUpdateConstants.Value; // update                    

            // получили все обновленные константы (веб может загружать сразу много словарем)
            // рассматриваем только обновления числовых констант, тестовые ключи и префиксы вряд ли имеет смысл менять
            // но, если что, можно это делать таким же методом, только с другим типом данных
            // вынесли сюда - (можно вынести словарь из метода и не вбок, а поставить до метода, чтобы CheckKeyUpdateConstants стал статическим)
            // ответ для веба - принята его команда или нет, удалить ключ сразу после считывания в словарь
            IDictionary<string, int> updatedConstants = await _cache.FetchUpdatedConstantsAndDeleteKey<string, int>(eventKeyUpdateConstants);
            if (updatedConstants == null)
            {
                // разрешаем события подписки на обновления
                return true;
            }

            // создание нового набора констант подтверждения, что изменения были внесены
            (bool setWasUpdated, ConstantsSet constantsSetUpdated) = CreateUpdatedConstantsSet(updatedConstants, appsettingLines);

            // если какая-то из констант обновилась, записываем новый набор в ключ
            if (setWasUpdated)
            {
                // версия констант обновится внутри SetStartConstants - new one
                // значения ключей надо брать из новых констант или из старых?
                string constantsVersionBaseKey = constantsSet.ConstantsVersionBaseKey.Value;
                double constantsVersionBaseLifeTime = constantsSet.ConstantsVersionBaseKey.LifeTime;

                // записываем обновленный набор констант constantsSetUpdated
                await _cache.SetStartConstants(constantsVersionBaseKey, constantsStartGuidField, constantsSetUpdated, constantsVersionBaseLifeTime);
            }

            // задержка, определяющая максимальную частоту обновления констант
            double timeToWaitTheConstants = constantsSet.EventKeyUpdateConstants.LifeTime;
            Logs.Here().Information("Delay will happen on {0} msec.", timeToWaitTheConstants);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(timeToWaitTheConstants), _cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Prevent throwing if the Delay is cancelled
            }
            Logs.Here().Information("Delay was happened on {0} msec.", timeToWaitTheConstants);
            
            // перед завершением обработчика разрешаем события подписки на обновления
            return true;
        }


        // ****************************************************************************
        // доделать обновление констант, проверить их с диспетчером и бэк-сервером
        // сделать загрузку книги в кэш (и, может быть, отметку об этом в вечном логе)
        // вернуться к чтению книг, сделать загрузку из кэша и собственно чтение
        // сделать юнит-тесты на всю статику
        // ****************************************************************************


        public static (bool, ConstantsSet) CreateUpdatedConstantsSet(IDictionary<string, int> updatedConstants, List<string> appsettingLines)
        {
            //// проверять (в вебе), что константы может обновлять только админ
            //// добавить разрешённый диапазон для изменения констант (где его хранить?)

            //// ключ, где брать обновления констант (и из веба тоже там будут)
            //string eventKeyUpdateConstants = constantsSet.EventKeyUpdateConstants.Value; // update
            //bool setWasUpdated = false;

            //// получили все обновленные константы (веб может загружать сразу много словарем)
            //// рассматриваем только обновления числовых констант, тестовые ключи и префиксы вряд ли имеет смысл менять
            //// но, если что, можно это делать таким же методом, только с другим типом данных
            //// можно вынести словарь из метода и не вбок, а поставить до метода, чтобы стал этот статическим
            //IDictionary<string, int> updatedConstants = await _cache.FetchUpdatedConstantsAndDeleteKey<string, int>(eventKeyUpdateConstants);
            //if (updatedConstants == null)
            //{
            //    // разрешаем события подписки на обновления
            //    return true;
            //}
            bool setWasUpdated = false;

            ConstantsSet constantsSetUpdated = new ConstantsSet();
            int updatedConstantNameIndex = -1;
            foreach (KeyValuePair<string, int> updatedConstant in updatedConstants)
            {
                // было бы хорошо сразу узнать, отличается новое значение от существующего, но это опять использовать Reflection
                var (updatedConstantName, updatedConstantValue) = updatedConstant;
                Logs.Here().Information("New value {0} for Property {1} was fetched.", updatedConstantValue, updatedConstantName);

                // найти в списке строку, содержащую updatedConstantName 
                updatedConstantNameIndex = appsettingLines.FindIndex(x => x.Contains(updatedConstantName));
                Logs.Here().Information("Property {0} was found in [{1}] - {2}.", updatedConstantName, updatedConstantNameIndex, appsettingLines[updatedConstantNameIndex]);

                // выйти и вернуть найденный updatedConstantNameIndex

                // выгружаем в json (key, value) все следующие строки после найденной, начинающиеся с " (может быть не первой)
                // или просто ищем после найденной строки строку "Value": (+ пробел) - все-таки структура очень строгая
                // еще можно выгрузить в json весь младший класс ConstantType - он как раз начинается с найденной строки - всего 5 строк
                // 5 - может быть количество полей класса ConstantType + 2 (заголовок и закрывающая скобка)
                // заголовок долой (и скобку тоже)
                // превращаем их в один текст (там еще может быть запятая в конце)
                // а еще бывает ConstantKeyType, но с ней пока не будем связываться
                // заголовок не нужен и занятая тоже, берём три строки после найденного названия и это почти готовый json
                // слить эти три элемента вместе и поставить по краям фигурные скобки - и можно конвертировать
                int propsCountOfConstantType = 3;
                string[] foundConstantClass = new string[propsCountOfConstantType];
                for (int i = 0; i < propsCountOfConstantType; i++)
                {
                    // сдвигаем на следующую строку относительно названия
                    foundConstantClass[i] = appsettingLines[updatedConstantNameIndex + 1 + i];
                }

                string textOfConstantTypeSource = String.Join(" ", foundConstantClass);
                string textOfConstantType = $"{{{textOfConstantTypeSource}}}";

                //Logs.Here().Information("foundConstantClass was joined in {@T}.", new { ConstantKeyTypeText = textOfConstantType });
                //Console.WriteLine($"{textOfConstantType}");

                //JsonSerializer serializer = new JsonSerializer();
                ConstantType constantNameToUpdate = JsonConvert.DeserializeObject<ConstantType>(textOfConstantType);
                Logs.Here().Information("old value = {0}, new value = {1} - in constantNameToUpdate {@K}.", constantNameToUpdate.Value, updatedConstantValue, new { ConstantKeyType = constantNameToUpdate });

                // обновляем значение и можно конвертировать обратно
                if (constantNameToUpdate.Value != updatedConstantValue)
                {
                    constantNameToUpdate.Value = updatedConstantValue;
                    // даже если одно значение изменили, всё равно уже true
                    setWasUpdated = true;
                }
                    string jsonString = JsonConvert.SerializeObject(constantNameToUpdate);
                
                //Logs.Here().Information("Text (json) string was created - {@J}.", new { ConstantKeyTypeToString = jsonString });
                //Console.WriteLine($"{jsonString}");

                string jsonStringBraces = jsonString.TrimStart('{').TrimEnd('}');

                //Console.WriteLine($"{jsonStringBraces}");
                //int screenFullWidthLinesCount = 228;
                //char screenFullWidthTopLineChar = '-';

                int part1Start = 0;
                int part1Lengh = updatedConstantNameIndex + 1;
                string constantSetFromText0 = String.Join(' ', appsettingLines.ToArray(), part1Start, part1Lengh);

                //Console.WriteLine(("").PadRight(screenFullWidthLinesCount, screenFullWidthTopLineChar));
                //Console.WriteLine($"\n{constantSetFromText0}\n");
                //Console.WriteLine(("").PadRight(screenFullWidthLinesCount, screenFullWidthTopLineChar));

                //Console.WriteLine($"\n{jsonStringBraces}\n");

                int part2Start = updatedConstantNameIndex + propsCountOfConstantType + 1;
                int part2Lengh = appsettingLines.Count - (updatedConstantNameIndex + propsCountOfConstantType) - 1;
                // откуда-то приплыла лишняя фигурная скобка в конце - надо разобраться
                string constantSetFromText1 = String.Join(' ', appsettingLines.ToArray(), part2Start, part2Lengh).TrimEnd('}');

                //Console.WriteLine(("").PadRight(screenFullWidthLinesCount, screenFullWidthTopLineChar));
                //Console.WriteLine($"\n{constantSetFromText1}\n");
                //Console.WriteLine(("").PadRight(screenFullWidthLinesCount, screenFullWidthTopLineChar));

                string constantSetFromTextUnited = $"{constantSetFromText0}{jsonStringBraces}{constantSetFromText1}";

                //Console.WriteLine($"\n{constantSetFromTextUnited}\n");

                
                constantsSetUpdated = JsonConvert.DeserializeObject<ConstantsSet>(constantSetFromTextUnited);
                //Logs.Here().Information("\n {@C} \n.", new { ConstantsSetUpdated = constantsSetUpdated });
                bool constantsSetIsHealthy = AuxiliaryUtilsService.CheckConstantSet(constantsSetUpdated);
                Logs.Here().Information("Constants health check result is {0}.", constantsSetIsHealthy);

                // вывести старое и новое значения (как это сделать для любого?)
                // для печати взять заехавшее поле из подписки и заодно найденную строку из текста
                // вызвать проверочный метод целостности набора констант
                // проверить с несколькими обновлениями сразу
            }

            // тут надо удалять поле, с которого считано обновление
            // или не удалять по одному, а на выходе всегда удалять ключ целиком - в любом случае
            // здесь удалить ключ, чтобы веб узнал о принятии данных
            //bool updateKeyWasRemoved = await _cache.DelKeyAsync(eventKeyUpdateConstants);
            //Logs.Here().Information("Key update was removed with result {0}.", updateKeyWasRemoved);

            //if (!updateKeyWasRemoved)
            //{
            //    _aux.SomethingWentWrong(updateKeyWasRemoved);
            //}

            // тогда юнит-тест останется живой

            return (setWasUpdated, constantsSetUpdated);
        }





        // --- LEGACY --

        private async Task<bool> CheckKeyUpdateConstants(ConstantsSet constantsSet, string constantsStartGuidField, CancellationToken stoppingToken) // Main of EventKeyFrontGivesTask key
        {
            // проверять, что константы может обновлять только админ

            string eventKeyUpdateConstants = constantsSet.EventKeyUpdateConstants.Value; // update
            Logs.Here().Debug("CheckKeyUpdateConstants started with key {0}.", eventKeyUpdateConstants);

            IDictionary<string, int> updatedConstants = await _cache.FetchUpdatedConstantsAndDeleteKey<string, int>(eventKeyUpdateConstants); ;
            int updatedConstantsCount = updatedConstants.Count;
            Logs.Here().Debug("Fetched updated constants count = {0}.", updatedConstantsCount);

            // выбирать все поля, присваивать по таблице, при присваивании поле удалять
            // все обновляемые константы должны быть одного типа или разные типы на разных ключах

            bool setWasUpdated;
            (setWasUpdated, constantsSet) = UpdatedValueAssignsToProperty(constantsSet, updatedConstants);
            if (setWasUpdated)
            {
                // версия констант обновится внутри SetStartConstants
                await _cache.SetStartConstants(constantsSet.ConstantsVersionBaseKey, constantsStartGuidField, constantsSet);
            }

            // задержка, определяющая максимальную частоту обновления констант
            double timeToWaitTheConstants = constantsSet.EventKeyUpdateConstants.LifeTime;
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(timeToWaitTheConstants), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Prevent throwing if the Delay is cancelled
            }
            // перед завершением обработчика разрешаем события подписки на обновления
            return true;
        }

        public static (bool, ConstantsSet) UpdatedValueAssignsToProperty(ConstantsSet constantsSet, IDictionary<string, int> updatedConstants)
        {
            bool setWasUpdated = false;
            string finalPropertyToSet = constantsSet.FinalPropertyToSet.Value; // Value
            // foreach перенесли внутрь метода, чтобы лишний раз не переписывать набор, если обновление имеет такое же значение
            // возможно, что со страницы всегда будет приезжать весь набор полей только с одним/несколькими изменёнными
            foreach (KeyValuePair<string, int> updatedConstant in updatedConstants)
            {
                var (key, value) = updatedConstant;

                int existsConstant = FetchValueOfPropertyOfProperty(constantsSet, finalPropertyToSet, key);
                // можно проверять предыдущее значение и, если новое такое же, не обновлять
                // но тогда надо проверять весь пакет и только если все не изменились, то не переписывать ключ
                // может быть когда-нибудь потом
                if (existsConstant != value)
                {
                    // но запись в ключ всё равно произойдёт, как это устранить?
                    //return constantsSet;

                    object constantType = FetchValueOfProperty(constantsSet, key);

                    if (constantType == null)
                    {
                        Logs.Here().Error("Wrong {@P} was used - update failed", new { PropertyName = key });
                        return (false, constantsSet);
                    }

                    constantType.GetType().GetProperty(finalPropertyToSet)?.SetValue(constantType, value);

                    int constantWasUpdated = FetchValueOfPropertyOfProperty(constantsSet, finalPropertyToSet, key);
                    if (constantWasUpdated == value)
                    {
                        setWasUpdated = true;
                    }
                }
                else
                {
                    // если не обновится ни одно поле, в setWasUpdated останется false и основной ключ не обновится
                    // ещё можно показать значения - бывшее и которое хотели обновить
                    Logs.Here().Warning("Constant {@K} will be left unchanged", new { Key = key });
                }
                // тут надо удалять поле, с которого считано обновление
                // или не удалять по одному, а на выходе всегда удалять ключ целиком - в любом случае
                // тогда юнит-тест останется живой
            }
            return (setWasUpdated, constantsSet);
        }

        private static int FetchValueOfPropertyOfProperty(ConstantsSet constantsSet, string finalPropertyToSet, string key)
        {
            int constantValue = Convert.ToInt32(FetchValueOfProperty(FetchValueOfProperty(constantsSet, key), finalPropertyToSet));
            Logs.Here().Information("The value of property {0} = {1}.", key, constantValue);
            return constantValue;
        }

        private static object FetchValueOfProperty(object classInstance, string propertyName)
        {
            return classInstance?.GetType().GetProperty(propertyName)?.GetValue(classInstance);
        }
    }
}
