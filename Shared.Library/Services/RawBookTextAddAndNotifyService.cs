using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shared.Library.Models;
using Shared.Library.Services;

namespace Shared.Library.Services
{
    public interface IRawBookTextAddAndNotifyService
    {
        public Task<bool> AddPainBookText(ConstantsSet constantsSet, TextSentence bookPlainTextWithDescription, string bookGuid);
    }

    public class RawBookTextAddAndNotifyService : IRawBookTextAddAndNotifyService
    {
        private readonly IAuxiliaryUtilsService _aux;
        private readonly ICacheManagerService _cache;

        public RawBookTextAddAndNotifyService(
            IAuxiliaryUtilsService aux,
            ICacheManagerService cache)
        {
            _aux = aux;
            _cache = cache;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<RawBookTextAddAndNotifyService>();


        public async Task<bool> AddPainBookText(ConstantsSet constantsSet, TextSentence bookPlainTextWithDescription, string bookGuid)
        {
            // хранить все хэши плоских текстов в специальном ключе
            // с полем номер/язык книги (со сдвигом другого языка)
            // это хорошо согласуется с идеей хранить логи загрузки плоских текстов
            // можно типа значения оставить TextSentence, добавить поле хэша, а сам плоский текст удалять для экономии места
            // версия будет присваиваться где-то дальше, после разбора на главы и предложения или прямо здесь?
            // старую версию оставить как есть - для совместимости
            // новую версию, через хэш, присваивать где-то в тестах - до создания события subscribeOnFrom
            // 

            // достать нужные префиксы, ключи и поля из констант
            // string bookPlainText_KeyPrefixGuid = $"{constantsSet.BookPlainTextConstant.KeyPrefixGuid.Value}:test"; //TEMP FOR TEST
            string bookPlainText_KeyPrefixGuid = constantsSet.BookPlainTextConstant.KeyPrefixGuid.Value;
            // double keyExistingTime = 1000; // constantsSet.BookPlainTextConstant.KeyPrefixGuid.LifeTime; //TEMP FOR TEST
            double keyExistingTime = constantsSet.BookPlainTextConstant.KeyPrefixGuid.LifeTime;

            // создать ключ/поле из префикса и гуид книги
            string bookPlainText_FieldPrefixGuid = $"{constantsSet.BookPlainTextConstant.FieldPrefix.Value}:{bookGuid}";

            // здесь проверить тестовый ключ и ждать, если идет тест
            bool isTestInProgress = await _aux.IsTestInProgress(constantsSet);
            if (isTestInProgress)
            {
                // выполняется тест и не освободился, надо возвращать пользователю отлуп
                Logs.Here().Information("The waiting time of the end of the test was expired - {0}", isTestInProgress);

                return false;
            }

            // записать текст в ключ bookPlainTextKeyPrefix + this Server Guid и поле bookTextFieldPrefix + BookGuid
            // перенести весь _access в Shared.Library.Services CacheManageService
            // ключ для хранения плоского текста книги из префикса BookTextFieldPrefix и bookTextSplit server Guid
            // поле представляет собой префикс bookText:bookGuid: + bookGuid и хранит в значении плоский текст книги с полным описанием 
            await _cache.WriteHashedAsync<TextSentence>(bookPlainText_KeyPrefixGuid, bookPlainText_FieldPrefixGuid, bookPlainTextWithDescription, keyExistingTime);
            Logs.Here().Information("Key was created - {@K} \n {@F} \n {@V} \n", new { Key = bookPlainText_KeyPrefixGuid }, new { Field = bookPlainText_FieldPrefixGuid }, new { ValueOfBookId = bookPlainTextWithDescription.BookId });



            // а как передать BookGuid бэк-серверу?
            // 1 никак, будет искать по всем полям
            // 2 через ключ оповещения подписки, поле сделать номером по синхронному счётчику, а в значении это самое поле книги
            // тогда меньше операций с ключами на стороне бэк-сервера - не надо каждый раз вытаскивать все поля (со значениями, между прочим), а сразу взять нужное
            // но как тогда синхронизировать счётчик?

            // записываем то же самое поле в ключ subscribeOnFrom, а в значение (везде одинаковое) - ключ всех исходников книг
            // на стороне диспетчера всё достать словарём и найти новое (если приедет много сразу из нескольких клиентов)
            // уже обработанное поле сразу удалить, чтобы не накапливались
            string eventKeyFrom = constantsSet.EventKeyFrom.Value;
            await _cache.WriteHashedAsync<string>(eventKeyFrom, bookPlainText_FieldPrefixGuid, bookPlainText_KeyPrefixGuid, keyExistingTime);
            Logs.Here().Information("Key was created - {@K} \n {@F} \n {@V} \n", new { Key = eventKeyFrom }, new { Field = bookPlainText_FieldPrefixGuid }, new { Value = bookPlainText_KeyPrefixGuid });

            // типа справились (тут можно подождать какой-то реакции диспетчера - скажем, когда исчезнет ключ subscribeOnFrom
            return true;
        }


    }
}
