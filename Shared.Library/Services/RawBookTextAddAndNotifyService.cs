using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shared.Library.Models;
using Shared.Library.Services;

namespace Shared.Library.Services
{
    public interface IRawBookTextAddAndNotifyService
    {
        public Task<bool> AddPainBookText(ConstantsSet constantsSet, TextSentence bookPlainTextWithDescription, string bookGuid, bool thisIsTheTest = false);
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

        // параметр bool thisIsTheTest нужен для записи тестовых книг в специальное хранилище для них
        // 
        public async Task<bool> AddPainBookText(ConstantsSet constantsSet, TextSentence bookPlainTextWithDescription, string bookGuid, bool thisIsTheTest = false)
        {
            // здесь проверить тестовый ключ и ждать, если идет тест
            bool isTestInProgress = await _aux.IsTestInProgress(constantsSet);
            if (isTestInProgress)
            {
                // выполняется тест и не освободился, надо возвращать пользователю отлуп
                Logs.Here().Error("The waiting time of the end of the test was expired - {0}", isTestInProgress);

                return false;
            }

            string eventKeyFrom = constantsSet.EventKeyFrom.Value;
            double keyExistingTimeFrom = constantsSet.EventKeyFrom.LifeTime;

            string bookPlainText_KeyPrefixGuid = "";
            double keyExistingTimePlain = 0;

            if (thisIsTheTest)
            {
                // bookPlainText_KeyPrefixGuid = $"{constantsSet.BookPlainTextConstant.KeyPrefixGuid.Value}:test"; //TEMP FOR TEST
                // keyExistingTimePlain = 1000; // constantsSet.BookPlainTextConstant.KeyPrefixGuid.LifeTime; //TEMP FOR TEST
            }
            else
            {
                bookPlainText_KeyPrefixGuid = constantsSet.BookPlainTextConstant.KeyPrefixGuid.Value;
                keyExistingTimePlain = constantsSet.BookPlainTextConstant.KeyPrefixGuid.LifeTime;
            }
            string bookPlainText_FieldPrefixGuid = $"{constantsSet.BookPlainTextConstant.FieldPrefix.Value}:{bookGuid}";
            string intBookGuid = bookPlainTextWithDescription.BookGuid;
            Logs.Here().Information("BookGuid comparing - {@E} / {@I}", new { ExtBookGuid = bookGuid }, new { IntBookGuid = intBookGuid });

            await _cache.WriteHashedAsync<TextSentence>(bookPlainText_KeyPrefixGuid, bookPlainText_FieldPrefixGuid, bookPlainTextWithDescription, keyExistingTimePlain);
            Logs.Here().Information("Key bookPlainText was created - {@K} \n {@F} \n {@V}", new { Key = bookPlainText_KeyPrefixGuid }, new { Field = bookPlainText_FieldPrefixGuid }, new { ValueOfBookId = bookPlainTextWithDescription.BookId });

            await _cache.WriteHashedAsync<string>(eventKeyFrom, bookPlainText_FieldPrefixGuid, bookPlainText_KeyPrefixGuid, keyExistingTimeFrom);
            Logs.Here().Information("Key was created - {@K} \n {@F} \n {@V}", new { Key = eventKeyFrom }, new { Field = bookPlainText_FieldPrefixGuid }, new { Value = bookPlainText_KeyPrefixGuid });

            // типа справились (тут можно подождать какой-то реакции диспетчера - скажем, когда исчезнет ключ subscribeOnFrom
            return true;
        }
    }
}
