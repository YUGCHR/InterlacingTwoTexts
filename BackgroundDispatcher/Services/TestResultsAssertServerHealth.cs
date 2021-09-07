using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Shared.Library.Models;
using Shared.Library.Services;

namespace BackgroundDispatcher.Services
{
    public interface ITestResultsAssertServerHealth
    {
        public Task<bool> EventCafeOccurred(ConstantsSet constantsSet, CancellationToken stoppingToken);
    }
    public class TestResultsAssertServerHealth : ITestResultsAssertServerHealth
    {
        private readonly CancellationToken _cancellationToken;
        private readonly IAuxiliaryUtilsService _aux;
        private readonly ICacheManagerService _cache;
        private readonly ITestMainServiceOfComplexIntegrity _test;
        private readonly ITestTimeImprintsReportIsFilledOut _report;

        public TestResultsAssertServerHealth(
            IHostApplicationLifetime applicationLifetime,
            IAuxiliaryUtilsService aux,
            ICacheManagerService cache,
            ITestMainServiceOfComplexIntegrity test,
            ITestTimeImprintsReportIsFilledOut report)
        {
            _cancellationToken = applicationLifetime.ApplicationStopping;
            _aux = aux;
            _cache = cache;
            _test = test;
            _report = report;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<TestResultsAssertServerHealth>();

        public async Task<bool> EventCafeOccurred(ConstantsSet constantsSet, CancellationToken stoppingToken)
        {
            // получен ключ кафе, секундомер рабочих процессов пора остановить
            long tsWork99 = _test.FetchWorkStopwatch();
            //bool isRequestedStopWatchTest = false;
            //long tsWork99 = _report.StopwatchesControlAndRead(isRequestedStopWatchTest, false);
            Logs.Here().Information("Books processing were finished. Work Stopwatch has been stopped and it is showing {0}", tsWork99);
            //// to reset
            //_ = _report.StopwatchesControlAndRead(isRequestedStopWatchTest, false);

            string cafeKey = constantsSet.Prefix.BackgroundDispatcherPrefix.EventKeyFrontGivesTask.Value; // key-event-front-server-gives-task-package

            // выгрузить содержимое ключа кафе и сразу вернуть true в подписку, чтобы освободить место для следующего вызова
            // и всё равно можно прозевать вызов
            // для надёжности надо вернуть true, а потом сразу выгрузить ключ кафе, тогда точно не пропустить второй вызов
            // для точности сделать eventCafeIsNotExisted полем класса и отсюда её поставить в true - а потом не спеша делать всё остальное
            IDictionary<string, string> taskPackageGuids = await _cache.FetchHashedAllAsync<string>(cafeKey);

            _ = AssertProcessedBookFieldsAreEqualToControl(constantsSet, taskPackageGuids, stoppingToken);

            return true;
        }

        // метод предварительного сбора результатов теста
        // получает ключ пакета, достаёт бук и что с ним делает?
        // можно создать ключ типа список книг теста (важен порядок или нет?) при старте теста
        // и при проверке прохождения теста удалять из него поля, предварительно сравнивая bookId, bookGuid и bookHash
        // как все поля исчезнут, так тест прошёл нормально - если, конечно, не осталось лишних на проверку после теста
        // в смысле, ненайденных в стартовом списке теста
        private async Task<bool> AssertProcessedBookFieldsAreEqualToControl(ConstantsSet constantsSet, IDictionary<string, string> taskPackageGuids, CancellationToken stoppingToken)
        {
            // добавить счётчик потоков и проверить при большом количестве вызовов
            string controlListOfTestBookFieldsKey = constantsSet.Prefix.IntegrationTestPrefix.ControlListOfTestBookFieldsKey.Value; // control-list-of-test-book-fields-key
            string assertProcessedBookAreEqualControl = constantsSet.Prefix.IntegrationTestPrefix.AssertProcessedBookAreEqualControl.Value; // assert-that-processed-book-fields-are-equal-to-control-books
            double keyExistingTime = constantsSet.Prefix.IntegrationTestPrefix.AssertProcessedBookAreEqualControl.LifeTime; // 0.007
            int remaindedFieldsCount = constantsSet.Prefix.IntegrationTestPrefix.RemaindedFieldsCount.ValueInt; // 1
            int testResultsField2 = constantsSet.Prefix.IntegrationTestPrefix.ResultsField2.ValueInt; // 2
            int testResultsField3 = constantsSet.Prefix.IntegrationTestPrefix.ResultsField3.ValueInt; // 3
            int remaindedFields = -1;

            foreach (var g in taskPackageGuids)
            {
                (string taskPackageGuid, string vG) = g;

                Logs.Here().Information("taskPackageGuid {0} was fetched.", taskPackageGuid);

                IDictionary<string, TextSentence> fieldValuesControl = await _cache.FetchHashedAllAsync<TextSentence>(controlListOfTestBookFieldsKey);
                int fieldValuesControlCount = fieldValuesControl.Count;

                IDictionary<string, TextSentence> fieldValuesResult = await _cache.FetchHashedAllAsync<TextSentence>(taskPackageGuid);
                int fieldValuesResultCount = fieldValuesResult.Count;
                int deletedFields = 0;
                Logs.Here().Information("fieldValuesResult with count {0} was fetched from taskPackageGuid.", fieldValuesResultCount);

                // write test asserted results in the report key
                foreach (KeyValuePair<string, TextSentence> p in fieldValuesResult)
                {
                    (string fP, TextSentence vP) = p;
                    Logs.Here().Debug("Field {0} was found in taskPackageGuid and will be deleted in key {1}.", fP, controlListOfTestBookFieldsKey);

                    bool result0 = await CheckAssertFieldsAreEqualToControlAndEternal(constantsSet, fP, vP, stoppingToken);

                    if (result0)
                    {
                        bool result1 = await _cache.DelFieldAsync(controlListOfTestBookFieldsKey, fP);
                        if (result1)
                        {
                            deletedFields++;
                            Logs.Here().Debug("The comparison returned {0} and field {1} / value {2} was sucessfully deleted in key {3}.", result0, fP, vP.BookId, controlListOfTestBookFieldsKey);
                        }
                    }
                }

                remaindedFields = fieldValuesResultCount - deletedFields;

                bool result2 = await _cache.IsKeyExist(controlListOfTestBookFieldsKey);
                if (!result2)
                {
                    Logs.Here().Information("There are no remained fields in key {0}. Test is completed (but does not know about it).", controlListOfTestBookFieldsKey);

                    // исчезнувший ключ - не вполне надёжное средство оповещения,
                    // поэтому надо записать ещё ключ testResultsKey1 и тест дополнительно проверит его
                    // WriteHashedAsync<TK, TV>(string key, IEnumerable<KeyValuePair<TK, TV>> fieldValues, double ttl)

                    IDictionary<int, int> fieldValues = new Dictionary<int, int>();

                    fieldValues.Add(remaindedFieldsCount, remaindedFields);
                    fieldValues.Add(testResultsField2, fieldValuesResultCount);
                    fieldValues.Add(testResultsField3, fieldValuesControlCount);

                    await _cache.WriteHashedAsync<int, int>(assertProcessedBookAreEqualControl, fieldValues, keyExistingTime);

                    return true;
                }
            }

            bool assertedResult = false;
            if (remaindedFields == 0)
            {
                assertedResult = true;
            }
            return assertedResult;
        }

        // 
        private async Task<bool> CheckAssertFieldsAreEqualToControlAndEternal(ConstantsSet constantsSet, string fP, TextSentence vP, CancellationToken stoppingToken)
        {
            string keyBookPlainTextsHashesVersionsList = constantsSet.Prefix.BackgroundDispatcherPrefix.EternalBookPlainTextHashesLog.Value; // key-book-plain-texts-hashes-versions-list
            string controlListOfTestBookFieldsKey = constantsSet.Prefix.IntegrationTestPrefix.ControlListOfTestBookFieldsKey.Value; // control-list-of-test-book-fields-key
            int chapterFieldsShiftFactor = constantsSet.ChapterFieldsShiftFactor.Value; // 1000000

            // здесь сравнить bookId, bookGuid и bookHash книг 3
            int bookId = vP.BookId;
            int languageId = vP.LanguageId;
            int bookHashVersion = vP.HashVersion;

            TextSentence bookPlainFromControl = await _cache.FetchHashedAsync<TextSentence>(controlListOfTestBookFieldsKey, fP);
            //Logs.Here().Information("{@C}.", new { Value = bookPlainFromControl });
            bool bookIdComparingWithControl = bookPlainFromControl.BookId == vP.BookId;
            bool bookGuidComparingWithControl = String.Equals(bookPlainFromControl.BookGuid, vP.BookGuid);

            // здесь ещё посмотреть и сравнить в вечном логе
            // здесь надо перевести bookId в вид со сдвигом
            int fieldBookIdWithLanguageId = bookId + languageId * chapterFieldsShiftFactor;
            Logs.Here().Debug("Check FetchHashedAsync<int, List<TextSentence>> - key {0}, field {1}, element {2}.", keyBookPlainTextsHashesVersionsList, fieldBookIdWithLanguageId, bookHashVersion);
            List<TextSentence> bookPlainTextsVersions = await _cache.FetchHashedAsync<int, List<TextSentence>>(keyBookPlainTextsHashesVersionsList, fieldBookIdWithLanguageId);
            TextSentence bookPlainFromEternalLog = bookPlainTextsVersions[bookHashVersion];
            //Logs.Here().Information("{@E} is bookPlainTextsVersions[{1}].", new { BookPlainFromEternalLog = bookPlainFromEternalLog }, bookHashVersion);

            bool bookIdComparingWithEternal = bookPlainFromEternalLog.BookId == vP.BookId;
            bool bookGuidComparingWithEternal = String.Equals(bookPlainFromEternalLog.BookGuid, vP.BookGuid);
            bool bookHashComparingWithEternal = String.Equals(bookPlainFromEternalLog.BookPlainTextHash, vP.BookPlainTextHash);
            bool bookHashVersionComparingWithEternal = bookPlainFromEternalLog.HashVersion == vP.HashVersion;

            bool result0 = bookIdComparingWithControl && bookGuidComparingWithControl && bookIdComparingWithEternal && bookGuidComparingWithEternal && bookHashComparingWithEternal && bookHashVersionComparingWithEternal;

            return result0;
        }
    }
}
