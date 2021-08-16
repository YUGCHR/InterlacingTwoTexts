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
        public Task<(string, string)> AddPlainTextGuidKey(ConstantsSet constantsSet, TextSentence bookPlainTextWithDescription, string bookGuid, bool thisIsTheTest = false);
        public Task<bool> AddEventKeyFrom(ConstantsSet constantsSet, string bookPlainText_KeyPrefixGuid, string bookPlainText_FieldPrefixGuid);
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

        // сохранить исходный метод
        // разделить его на два метода - 
        // 1 создание ключа с книгой
        // 2 создание ключа оповещения
        // оба метода в интерфейс
        // в исходном методе вызвать их оба
        public async Task<bool> AddPainBookText(ConstantsSet constantsSet, TextSentence bookPlainTextWithDescription, string bookGuid)
        {
            // здесь проверить тестовый ключ и ждать, если идет тест
            bool isTestInProgress = await _aux.IsTestInProgress(constantsSet);
            if (isTestInProgress)
            {
                // выполняется тест и не освободился, надо возвращать пользователю отлуп
                Logs.Here().Information("The waiting time of the end of the test was expired - {0}", isTestInProgress);

                return false;
            }

            (string bookPlainText_KeyPrefixGuid, string bookPlainText_FieldPrefixGuid) = await AddPlainTextGuidKey(constantsSet, bookPlainTextWithDescription, bookGuid);
            bool resultFrom = await AddEventKeyFrom(constantsSet, bookPlainText_KeyPrefixGuid, bookPlainText_FieldPrefixGuid);

            // типа справились (тут можно подождать какой-то реакции диспетчера - скажем, когда исчезнет ключ subscribeOnFrom
            return resultFrom; // (true)
        }

        // параметр bool thisIsTheTest нужен для записи тестовых книг в специальное хранилище для них
        // записать текст в ключ bookPlainTextKeyPrefix + this Server Guid и поле bookTextFieldPrefix + BookGuid
        // 
        public async Task<(string, string)> AddPlainTextGuidKey(ConstantsSet constantsSet, TextSentence bookPlainTextWithDescription, string bookGuid, bool thisIsTheTest = false)
        {
            string bookPlainText_KeyPrefixGuid = "";
            double keyExistingTime = 0;
            if (thisIsTheTest)
            {
                // bookPlainText_KeyPrefixGuid = $"{constantsSet.BookPlainTextConstant.KeyPrefixGuid.Value}:test"; //TEMP FOR TEST
                // keyExistingTime = 1000; // constantsSet.BookPlainTextConstant.KeyPrefixGuid.LifeTime; //TEMP FOR TEST
            }
            else
            {
                bookPlainText_KeyPrefixGuid = constantsSet.BookPlainTextConstant.KeyPrefixGuid.Value;
                keyExistingTime = constantsSet.BookPlainTextConstant.KeyPrefixGuid.LifeTime;
            }

            string bookPlainText_FieldPrefixGuid = $"{constantsSet.BookPlainTextConstant.FieldPrefix.Value}:{bookGuid}";

            await _cache.WriteHashedAsync<TextSentence>(bookPlainText_KeyPrefixGuid, bookPlainText_FieldPrefixGuid, bookPlainTextWithDescription, keyExistingTime);
            Logs.Here().Information("Key was created - {@K} \n {@F} \n {@V} \n", new { Key = bookPlainText_KeyPrefixGuid }, new { Field = bookPlainText_FieldPrefixGuid }, new { ValueOfBookId = bookPlainTextWithDescription.BookId });

            return (bookPlainText_KeyPrefixGuid, bookPlainText_FieldPrefixGuid);
        }

        // записываем то же самое поле в ключ subscribeOnFrom, а в значение (везде одинаковое) - ключ всех исходников книг
        public async Task<bool> AddEventKeyFrom(ConstantsSet constantsSet, string bookPlainText_KeyPrefixGuid, string bookPlainText_FieldPrefixGuid)
        {
            string eventKeyFrom = constantsSet.EventKeyFrom.Value;
            double keyExistingTime = constantsSet.EventKeyFrom.LifeTime;
            await _cache.WriteHashedAsync<string>(eventKeyFrom, bookPlainText_FieldPrefixGuid, bookPlainText_KeyPrefixGuid, keyExistingTime);
            Logs.Here().Information("Key was created - {@K} \n {@F} \n {@V} \n", new { Key = eventKeyFrom }, new { Field = bookPlainText_FieldPrefixGuid }, new { Value = bookPlainText_KeyPrefixGuid });

            return true;
        }
    }
}
