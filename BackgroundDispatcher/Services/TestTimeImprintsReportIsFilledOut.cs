using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Shared.Library.Models;
using Shared.Library.Services;

// потом на диаграмме можно выстроить всю цепочку в линию, а по времени совместить с другими цепочками ниже
// можно генерировать выходной отчет в формате диаграммы - более реально - тайм-лайн для веба

// сделать вывод результатов отчета - пока что текстом в консоли, но чтобы можно было легко посмотреть

namespace BackgroundDispatcher.Services
{
    public interface ITestTimeImprintsReportIsFilledOut
    {
        Task<bool> AddStageToTestTaskProgressReport(ConstantsSet constantsSet, TestReport.TestReportStage sendingTestTimingReportStage);
        Task<bool> ProcessReportsListFromSourceStages(ConstantsSet constantsSet, int testScenario, long tsTest99);
        Task<bool> ViewComparedReportInConsole(ConstantsSet constantsSet, long tsTest99, int testScenario);
        bool Reset_stageReportFieldCounter();
    }

    public class TestTimeImprintsReportIsFilledOut : ITestTimeImprintsReportIsFilledOut
    {
        private readonly CancellationToken _cancellationToken;
        private readonly IEternalLogSupportService _eternal;
        private readonly IAuxiliaryUtilsService _aux;
        private readonly ICacheManagerService _cache;

        public TestTimeImprintsReportIsFilledOut(
            IHostApplicationLifetime applicationLifetime,
            IEternalLogSupportService eternal,
            IAuxiliaryUtilsService aux,
            ICacheManagerService cache)
        {
            _cancellationToken = applicationLifetime.ApplicationStopping;
            _eternal = eternal;
            _aux = aux;
            _cache = cache;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<TestTimeImprintsReportIsFilledOut>();

        private int _stageReportFieldCounter;
        private int _callingNumOfAddStageToTestTaskProgressReport;

        public bool Reset_stageReportFieldCounter()
        {
            // сбросить счетчик текущего шага тестового отчета по таймингу - только он в другом классе
            _stageReportFieldCounter = 0;

            if (_stageReportFieldCounter == 0)
            {
                return true;
            }
            return false;
        }

        // этот метод вызывается только из рабочих методов других классов
        // он получает имя рабочего метода currentMethodName, выполняющего тест в данный момент, номер тестовой цепочки и остальное в TestReport.TestReportStage testTimingReportStage
        // еще он собирает собственный счетчик вызовов, определяет номер шага отчета и делает засечки с двух (пока одного) таймеров
        public async Task<bool> AddStageToTestTaskProgressReport(ConstantsSet constantsSet, TestReport.TestReportStage sendingTestTimingReportStage)
        {
            // изменить названия точек методов на номера точек по порядку - можно синтезировать при печати имяМетода-1
            // сделать сначала текстовую таблицу, без тайм - лайн
            // для тайм-лайн надо синтезировать времена выполнения всего метода с данным номером задачи - начало, важный узел и конец метода

            string truncatedWorkActionName = sendingTestTimingReportStage.WorkActionName;
            int truncatedWorkActionNamePosition = sendingTestTimingReportStage.WorkActionName.IndexOf('=');
            if (truncatedWorkActionNamePosition > 0)
            {
                truncatedWorkActionName = sendingTestTimingReportStage.WorkActionName.Substring(0, truncatedWorkActionNamePosition);
            }

            // определяем собственно номер шага текущего отчета
            int count = Interlocked.Increment(ref _stageReportFieldCounter);

            // еще полезно иметь счетчик вызовов - чтобы определить многопоточность
            int lastCountStart = Interlocked.Increment(ref _callingNumOfAddStageToTestTaskProgressReport);

            // еще можно получать и записывать номер потока, в котором выполняется этот метод
            TestReport.TestReportStage testTimingReportStage = new TestReport.TestReportStage()
            {
                // StageId - номер шага с записью отметки времени теста, он же номер поля в ключе записи текущего отчета
                StageReportFieldCounter = count,
                // серийный номер единичной цепочки теста - обработка одной книги от события From
                ChainSerialNumber = sendingTestTimingReportStage.ChainSerialNumber,
                // Current Test Serial Number for this Scenario - номер теста в пакете тестов по данному сценарию, он же индекс в списке отчетов
                TheScenarioReportsCount = -1,
                // хеш шага отчета
                StageReportHash = "",
                // отметка времени от старта рабочей цепочки
                TsWork = sendingTestTimingReportStage.TsWork,
                // отметка времени от начала теста
                // можно запросить время (тоже рабочее) прямо отсюда и разница будет временем выполнения записи шага -
                // непонятно, зачем, но один раз интересно посмотреть
                TsTest = sendingTestTimingReportStage.TsTest,
                // имя вызвавшего метода, полученное в параметрах
                MethodNameWhichCalled = sendingTestTimingReportStage.MethodNameWhichCalled,
                // ключевое значение метода
                WorkActionNum = sendingTestTimingReportStage.WorkActionNum,
                // ключевое значение bool
                WorkActionVal = sendingTestTimingReportStage.WorkActionVal,
                // название ключевого значения метода
                //WorkActionName = sendingTestTimingReportStage.WorkActionName,
                WorkActionName = truncatedWorkActionName,
                // номер контрольной точки в методе, где считывается засечка времени
                ControlPointNum = sendingTestTimingReportStage.ControlPointNum,
                // количество одновременных вызовов принимаемого рабочего метода
                CallingCountOfWorkMethod = sendingTestTimingReportStage.CallingCountOfWorkMethod,
                // количество одновременных вызовов этого метода (AddStageToTestTaskProgressReport)
                CallingCountOfThisMethod = lastCountStart
            };

            // посчитаем хеш данных шага отчета для последующего сравнения версий 
            // storage-key-for-current-test-report / 0.01 - ключ хранения шагов отчета
            KeyType currentTestReportKeyTime = constantsSet.Prefix.IntegrationTestPrefix.CurrentTestReportKey;
            bool result = await AddHashToStageAndWrite(testTimingReportStage, currentTestReportKeyTime);

            int lastCountEnd = Interlocked.Decrement(ref _callingNumOfAddStageToTestTaskProgressReport);

            return result;
        }

        private async Task<bool> AddHashToStageAndWrite(TestReport.TestReportStage testTimingReportStage, KeyType currentTestReportKeyTime)
        {
            string currentTestReportKey = currentTestReportKeyTime.Value; // storage-key-for-current-test-report
            //double currentTestReportKeyExistingTime = currentTestReportKeyTime.LifeTime; // 0.01
            double currentTestReportKeyExistingTime = 1; // временное значение на время отладки
            int count = testTimingReportStage.StageReportFieldCounter;
            Logs.Here().Information("AddStageToTestTaskProgressReport was called by {0} on chain {1}.", testTimingReportStage.MethodNameWhichCalled, testTimingReportStage.ChainSerialNumber);

            // перенести усечение строки в вычисление хешей
            string truncatedWorkActionName = testTimingReportStage.WorkActionName;
            int truncatedWorkActionNamePosition = testTimingReportStage.WorkActionName.IndexOf('=');
            if (truncatedWorkActionNamePosition > 0)
            {
                truncatedWorkActionName = testTimingReportStage.WorkActionName.Substring(0, truncatedWorkActionNamePosition);
            }

            string s1 = testTimingReportStage.ChainSerialNumber.ToString();
            //string s2 = testTimingReportStage.TheScenarioReportsCount.ToString(); // ?
            string s3 = $"{testTimingReportStage.MethodNameWhichCalled}-{testTimingReportStage.ControlPointNum}";
            //string s4 = testTimingReportStage.WorkActionNum.ToString();
            string s5 = $"{truncatedWorkActionName}-{testTimingReportStage.WorkActionNum} ({testTimingReportStage.WorkActionVal})";
            //string s6 = testTimingReportStage.WorkActionName;
            //string s7 = testTimingReportStage.ControlPointNum;
            string unitedStageData = $"{s1}_{s3}_{s5}";

            testTimingReportStage.StageReportHash = AuxiliaryUtilsService.CreateMD5(unitedStageData);

            //string stageReportHash = AuxiliaryUtilsService.CreateMD5(unitedStageData);
            //testTimingReportStage.StageReportHash = stageReportHash;
            //Console.WriteLine(("").PadRight(150, '*'));
            //Logs.Here().Information("{0}). unitedStageData {1} has hash {2}.", count, unitedStageData, stageReportHash);
            //Console.WriteLine(("").PadRight(150, '+'));

            await _cache.WriteHashedAsync<int, TestReport.TestReportStage>(currentTestReportKey, count, testTimingReportStage, currentTestReportKeyExistingTime);
            Logs.Here().Information("Method Name Which Called {0} was written in field {1}.", testTimingReportStage.MethodNameWhichCalled, count);

            return true;
        }

        // сохраняется избыточное количество одинаковых тестов при наличии выбранного эталона
        // взошедший на трон эталон должен удалить всех своих двойников, кроме вновь прибывающих
        // MAIN - обработка отчетов, выбор эталона, формирование и запись списка, печать сводной таблицы
        // сборка отчетов из пошаговых списков и сохранение их списка в ключе
        public async Task<bool> ProcessReportsListFromSourceStages(ConstantsSet constantsSet, int testScenario, long tsTest99)
        {
            // создали список (из словаря, который из ключа) текущих измерений контрольных точек (внутренний список отсчета)
            (List<TestReport.TestReportStage> testTimingReportStagesList, string testReportHash) = await ConvertDictionaryWithReportToList(constantsSet);

            // получаем список отчетов по данному сценарию, чтобы в конце теста в него дописать текущий отчет
            List<TestReport> reportsListOfTheScenario = await LooksScenarioReportsListToAddCurrent(constantsSet, testScenario);

            string description_0 = "0 - The List Before Time";
            bool resView1 = ViewListOfReportsInConsole(constantsSet, description_0, testScenario, reportsListOfTheScenario);

            // 3/5 - взять из констант, назвать типа количество отчетов для начала проведения сравнения - ReportsCountToStartComparison
            int reportsCountToStartComparison = 3;
            // достаточное количество последовательных совпадающих отчетов, чтобы удалить дальнейшие несовпадающие
            int equalReportsCountToRemoveDifferents = 2;

            // ------------------------------------------------------------------------------------------------------------------------------------------------------------------------
            // достали список, проверили длину
            // может быть ситуация, когда списка еще не было и его создал LooksScenarioReportsListToAddCurrent с пустышкой (в нулевом индексе)
            // тогда сразу идем к действиям, создаем и записываем новый отчет в конец списка как есть (без версии)
            // записываем список в ключ и заканчиваем - return true
            // если длина больше 1 - хоть один настоящий отчет в списке уже был, переходим к анализу
            // если в списке один элемент - это пустышка, сразу переходим к созданию и записи текущего отчета в конец списка
            // сначала можно проверить нулевой индекс - есть ли там эталон
            // если эталон есть, надо сравнить хеш нового отчета и эталона
            // если совпадают, создать новый отчет с версией эталона
            // затем надо проверить весь список на наличие отчетов без признака эталона и все их надо удалить (ситуация 1)
            // потом записать в конец новый отчет (с версией эталона)
            // если эталона нет, начинаем подсчет количества отчетов без версий - это сырые отчеты, для которых еще не создавался эталон
            // с версией ожидаемо могут быть только старые эталоны, но в таком случае будет и действующий - не наш случай
            // наверное, можно просто считать отчеты, не проверяя версии - надо подумать
            // нет, в этом месте может быть ситуация, что эталон есть, но текущий отчет с ним не совпадает,
            // тогда в списке может быть произвольное количество эталонов и, как минимум, один отчет с версией действующего эталона
            // и удалим мы этот текущий отчет с версией только при смене эталона (см. ситуацию 1)
            // одновременно с подсчетом без версий (в том же цикле) сравниваем хеши нового и существующих отчетов, увеличивая счетчик одинаковых отчетов (без версий, понятное дело)
            // одинаковые отчеты должны идти подряд (интересует только непрерывный ряд одинаковых), первый же другой останавливает счетчик, даже если дальше будет еще такие же
            // получив счетчик количества отчетов без версий и счетчик одинаковых отчетов подряд, сравниваем их с заданными константами значениями
            // если отчетов без версий недостаточно, сразу переходим к созданию и записи текущего отчета в конец списка
            // если хватает, сравниваем одинаковые, если совсем мало, сразу переходим к созданию и записи текущего отчета в конец списка
            // если одинаковых отчетов еще мало для создания эталона, но уже достаточно для удаления несовпадающих, переходим к удалению
            // удаляем все отчеты с отрицательной версией (без версии) и (&&) с хешем, несовпадающим с текущим отчетом
            // при этом важный момент - по этим условиям удалится и пустышка [0], а этого нельзя допустить
            // два варианта - или потом записать ее заново или временно присвоить ей версию (скажем, 1000)
            // выберем первый вариант, перед массовым удалением прочитаем, а потом (сначала проверим наличие?) запишем
            // далее переходим к созданию и записи текущего отчета в конец списка
            // и последний вариант - отчетов (без версий и одинаковых) хватает для создания эталона
            // создание эталона
            // проверить наличие эталона, узнать его версию и увеличить ее на 1 для дальнейшего применения
            // если эталона нет, присвоить версию 1 - будет новый эталон
            // если в нулевом индексе была пустышка (isRefExisted == false), удаляем ее
            // создаем новый эталон с выбранной версией
            // записываем эталон в индекс 0 и текущий отчёт в конец списка
            // ------------------------------------------------------------------------------------------------------------------------------------------------------------------------

            // если в списке уже что-то есть, кроме добавленной пустышки в пустой, изучаем элементы списка
            int reportsListOfTheScenarioCount = reportsListOfTheScenario.Count;
            if (reportsListOfTheScenarioCount > 1)
            {
                bool writeResult_1 = await CheckReferenceToAssignVersion(constantsSet, testScenario, reportsListOfTheScenario, testTimingReportStagesList, testReportHash);
                if (writeResult_1)
                {
                    // запись в ключ произведена
                    return true;
                }

                // а здесь находимся, потому что эталона нет, надо узнавать насчет его создания
                // reportsWOversionsCount - счетчик количества отчетов без версий (с нулевой версией) - для определения готовности к спариванию
                // equalReportsCount - счетчик одинаковых отчетов подряд, чтобы определить, что пришло время создавать эталон
                (int reportsWOversionsCount, int equalReportsCount) = CountEqualWOversionReports(reportsListOfTheScenario, testReportHash);
                Logs.Here().Information("CountEqualWOversionReports returned reportsWOversionsCount = {0} and equalReportsCount = {1}.", reportsWOversionsCount, equalReportsCount);

                // рассмотрим варианты - 
                // 1 reportsWOversionsCount < reportsCountToStartComparison - просто добавляем отчет без версии
                // 2 reportsWOversionsCount >=, equalReportsCount < reportsCountToStartComparison - проверяем equalReportsCount >= equalReportsCountToRemoveDifferents -
                // удаляем все без версий, но несовпадающие с двумя (equalReportsCountToRemoveDifferents) последними
                // тут еще надо бы искать совпадающие подряд, а если раз не совпало, дальше уже не искать
                // 3 оба больше (равны на самом деле) - можно создавать эталон

                // отчетов без версий достаточное количество для обработки (сравнение хешей)
                if (reportsWOversionsCount >= reportsCountToStartComparison)
                {
                    // одинаковых отчетов достаточно для создания эталона
                    if (equalReportsCount >= reportsCountToStartComparison)
                    {
                        // здесь надо создать (возможно новый) эталонный отчет, а текущий отчет записать в конец списка
                        // запись в ключ произведена - return true
                        return await CreateNewReference(constantsSet, testScenario, reportsListOfTheScenario, testTimingReportStagesList, testReportHash);
                    }
                    // одинаковых отчетов еще мало для захвата власти, но уже достаточно для подавления несогласных
                    if (equalReportsCount >= equalReportsCountToRemoveDifferents)
                    {
                        string description_7 = "7 - will delete all reports with no version and with a hash that does not match the current report";
                        bool resView_7 = ViewListOfReportsInConsole(constantsSet, description_7, testScenario, reportsListOfTheScenario);

                        // удаляем всех без версий, но несовпадающие с equalReportsCountToRemoveDifferents последними
                        // важный момент - тут удалится и пустышка [0], этого нельзя допустить
                        TestReport tempZeroIndexEmpty = reportsListOfTheScenario[0];
                        reportsListOfTheScenario.RemoveAll(r => r.ThisReporVersion <= 0 && !String.Equals(testReportHash, r.ThisReportHash));

                        string description_7a = "7a - deleted all reports according to the above conditions including index[0]";
                        bool resView_7a = ViewListOfReportsInConsole(constantsSet, description_7a, testScenario, reportsListOfTheScenario);

                        reportsListOfTheScenario.Insert(0, tempZeroIndexEmpty);

                        string description_7b = "7b - index[0] was restored";
                        bool resView_7b = ViewListOfReportsInConsole(constantsSet, description_7b, testScenario, reportsListOfTheScenario);

                        // обновляем длину списка
                        reportsListOfTheScenarioCount = reportsListOfTheScenario.Count;
                    }
                }
            }
            // отчетов в списке нет вообще или
            // отчетов без версий недостаточное количество для обработки (проведения сравнения хешей) и, возможно, по дороге удалили всех несогласных
            // создаем и добавляем новый отчет в конец списка без версии
            TestReport theReportOfTheScenario = CreateNewTestReport(testScenario, reportsListOfTheScenarioCount, false, -1, testTimingReportStagesList, testReportHash);
            reportsListOfTheScenario.Add(theReportOfTheScenario);

            string description_5 = "5 - add a new report to the end of the list without version";
            bool resView_5 = ViewListOfReportsInConsole(constantsSet, description_5, testScenario, reportsListOfTheScenario);

            bool writeResult = await WriteTestScenarioReportsList(constantsSet, testScenario, reportsListOfTheScenario);
            // запись в ключ произведена
            return true;
        }

        // создание эталона
        // проверить наличие эталона, узнать его версию и увеличить ее на 1 для дальнейшего применения
        // если эталона нет, присвоить версию 1 - будет новый эталон
        // если в нулевом индексе была пустышка (isRefExisted == false), удаляем ее
        // создаем новый эталон с выбранной версией
        // записываем эталон в индекс 0 и текущий отчёт в конец списка
        private async Task<bool> CreateNewReference(ConstantsSet constantsSet, int testScenario, List<TestReport> reportsListOfTheScenario, List<TestReport.TestReportStage> testTimingReportStagesList, string testReportHash)
        {
            int reportsListOfTheScenarioCount = reportsListOfTheScenario.Count;
            // здесь надо проверить наличие эталона, узнать его версию и увеличить ее на 1 для дальнейшего применения
            // если эталона нет, присвоить версию 1

            string description_2s = "2s (NEW) reference report will be created soon";
            bool resView_2s = ViewListOfReportsInConsole(constantsSet, description_2s, testScenario, reportsListOfTheScenario);

            int maxVersion = reportsListOfTheScenario[0].ThisReporVersion;
            bool isRefExisted = reportsListOfTheScenario[0].IsThisReportTheReference && maxVersion > 0;
            if (isRefExisted)
            {
                maxVersion++;
            }
            else
            {
                maxVersion = 1;
                // а вот старый удалять не надо, удалить надо только пустышку
                reportsListOfTheScenario.RemoveAt(0);
                Logs.Here().Information("isRefExisted = {0} and maxVersion was set {1}.", isRefExisted, maxVersion);
            }

            // создаем новый эталон
            TestReport theScenarioReportRef = CreateNewTestReport(testScenario, 0, true, maxVersion, testTimingReportStagesList, testReportHash);
            
            string description_2r = "2r - RemoveAt(0) may has happened";
            bool resView_2r = ViewListOfReportsInConsole(constantsSet, description_2r, testScenario, reportsListOfTheScenario);

            // записываем новый эталон в нулевой индекс
            reportsListOfTheScenario.Insert(0, theScenarioReportRef);

            //вот тут удалить всех несогласных и децимировать согласных
            reportsListOfTheScenario.RemoveAll(r => !r.IsThisReportTheReference);

            // создаем текущий отчет и записываем его в конец с версией нового эталона
            TestReport theReportOfTheScenarioVerNewRef = CreateNewTestReport(testScenario, reportsListOfTheScenarioCount, false, maxVersion, testTimingReportStagesList, testReportHash);
            reportsListOfTheScenario.Add(theReportOfTheScenarioVerNewRef);

            Logs.Here().Information("theReportOfTheScenario was added with version {0}.", maxVersion);

            string description_2e = "2e (NEW) reference report was set in [0] and current report was added at the end with the same version";
            bool resView_2e = ViewListOfReportsInConsole(constantsSet, description_2e, testScenario, reportsListOfTheScenario);

            // здесь вызвать запись в ключ и закончить (return true - запись в ключ произведена)
            await WriteTestScenarioReportsList(constantsSet, testScenario, reportsListOfTheScenario);

            return true;
        }

        // получаем список отчетов по данному сценарию, чтобы в конце теста в него дописать текущий отчет
        // также этот метод устанавливает текущую версию теста в поле класса - нет
        private async Task<List<TestReport>> LooksScenarioReportsListToAddCurrent(ConstantsSet constantsSet, int testScenario)
        {
            int fieldBookIdWithLanguageId = testScenario;
            string eternalTestTimingStagesReportsLog = constantsSet.Prefix.IntegrationTestPrefix.EternalTestTimingStagesReportsLog.Value; // key-test-reports-timing-imprints-list

            (List<TestReport> reportsListOfTheScenario, int reportsListOfTheScenarioCount) = await _eternal.EternalLogAccess<TestReport>(eternalTestTimingStagesReportsLog, fieldBookIdWithLanguageId);

            if (reportsListOfTheScenarioCount == 0)
            {
                // надо создать пустой первый элемент (вместо new TestReport()), который потом можно заменить на эталонный
                List<TestReport.TestReportStage> testReportStages = new List<TestReport.TestReportStage>()
                {
                    new TestReport.TestReportStage()
                    {
                        TheScenarioReportsCount = 0
                    }
                };

                TestReport testReportForScenario = CreateNewTestReport(testScenario, 0, false, -1, testReportStages, "");
                // записываем пустышку, только если список пуст
                reportsListOfTheScenario.Add(testReportForScenario);
            }
            return reportsListOfTheScenario;
        }

        // метод создает и возвращает новый отчет TestReport testReportForScenario для списка отчетов, записывая в него -
        // testScenario - номер сценария (он же - поле в ключе списков отчетов),
        // theScenarioReportsCount - номер отчета в этом сценарии (он же индекс этого нового отчета в списке),
        // isThisReportTheReference - флаг, является ли этот отчет эталоном,
        // thisReporVersion - версия отчета (для эталона и последнего сохраняемого, даже если совпадает с эталоном),
        // testReportStages - собственно свежий список контрольных точек последнего отчета,
        // thisReportHash - хеш этого списка (без времени точек)
        // кроме того, внутри метода создается описание - обычный отчет или эталонный и
        // номер отчета (индекс) заносится во все элементы внутреннего списка контрольных точек - возможно, временно - для отображения в таблице сразу всего списка отчетов по сценарию
        // в дальнейшем передать сюда еще константы для теста описания
        private TestReport CreateNewTestReport(int testScenario, int theScenarioReportsCount, bool isThisReportTheReference, int thisReporVersion, List<TestReport.TestReportStage> testReportStages, string thisReportHash)
        {
            string referenceTestDescription = $"Reference test report for Scenario {testScenario}";
            string currentTestDescription = $"Current test report for Scenario {testScenario}";
            string testDescription(bool isThisReportTheReference) => isThisReportTheReference ? referenceTestDescription : currentTestDescription;

            Logs.Here().Information("Current test report was created - Scenario {0} Index {1}, isRef {2}, Version {3}, Stages {4}, Hash {5}.", testScenario, theScenarioReportsCount, isThisReportTheReference, thisReporVersion, testReportStages.Count, thisReportHash);

            TestReport testReportForScenario = new TestReport()
            {
                TestScenarioNum = testScenario,
                Guid = testDescription(isThisReportTheReference),
                TheScenarioReportsCount = theScenarioReportsCount,
                IsThisReportTheReference = isThisReportTheReference,
                ThisReporVersion = thisReporVersion,
                TestReportStages = testReportStages.ConvertAll(x => { x.TheScenarioReportsCount = theScenarioReportsCount; return x; }),
                ThisReportHash = thisReportHash
            };
            return testReportForScenario;
        }

        private (int, int) CountEqualWOversionReports(List<TestReport> reportsListOfTheScenario, string testReportHash)
        {
            bool wereConsistentlyEqual = true;
            // счетчик количества отчетов без версий (с нулевой версией) - для определения готовности к спариванию
            int reportsWOversionsCount = 0;
            // счетчик одинаковых отчетов, чтобы определить, что пришло время создавать эталон
            int equalReportsCount = 0;
            int reportsListOfTheScenarioCount = reportsListOfTheScenario.Count;

            for (int i = reportsListOfTheScenarioCount - 1; i > 0; i--)
            {
                // проверяем очередной отчет из списка на наличие версии, если ее нет, идем внутрь
                if (reportsListOfTheScenario[i].ThisReporVersion <= 0)
                {
                    // увеличиваем счетчик отчетов без версий (типа, свежих и несравненных)
                    reportsWOversionsCount++;

                    // сравниваем текущий хеш с проверяемым отчетом, если хеши одинаковые, увеличиваем счетчик совпадающих отчетов
                    // тут еще надо бы искать совпадающие подряд, а если раз не совпало, дальше уже не искать
                    bool reportsInPairAreEqual = String.Equals(testReportHash, reportsListOfTheScenario[i].ThisReportHash);
                    if (reportsInPairAreEqual && wereConsistentlyEqual)
                    {
                        equalReportsCount++;
                    }
                    else
                    {
                        wereConsistentlyEqual = false;
                    }
                }
            }
            return (reportsWOversionsCount, equalReportsCount);
        }

        // check for the reference to assign version
        // сначала можно проверить нулевой индекс - есть ли там эталон
        // если эталон есть, надо сравнить хеш нового отчета и эталона
        // если совпадают, создать новый отчет с версией эталона
        // затем надо проверить весь список на наличие отчетов без признака эталона и все их надо удалить (ситуация 1)
        // потом записать в конец новый отчет (с версией эталона)
        private async Task<bool> CheckReferenceToAssignVersion(ConstantsSet constantsSet, int testScenario, List<TestReport> reportsListOfTheScenario, List<TestReport.TestReportStage> testTimingReportStagesList, string testReportHash)
        {
            // если нулевой элемент списка является эталоном, проверяем текущий отчет на совпадение с эталонным
            // если да, то присвоить текущему обычному отчету версию эталона, записать его в конец списка и записать в ключ
            int maxVersion = -1;
            int refIndex = 0;
            int reportsListOfTheScenarioCount = reportsListOfTheScenario.Count;
            bool isThisReportTheReference = reportsListOfTheScenario[refIndex].IsThisReportTheReference;
            Logs.Here().Information("check index[0] - is it the reference - {0}.", isThisReportTheReference);

            if (isThisReportTheReference)
            {
                maxVersion = reportsListOfTheScenario[refIndex].ThisReporVersion;
                bool theReportsWithRefAreEqual = String.Equals(testReportHash, reportsListOfTheScenario[refIndex].ThisReportHash);
                Logs.Here().Information("maxVersion = {0}, hashes are the same - {1}.", maxVersion, theReportsWithRefAreEqual);

                if (theReportsWithRefAreEqual)
                {
                    // действующий эталон устраивает, текущий отчет записать в конец с версией этого эталона
                    // в этой конфигурации список должен состоять из действующего эталона в нулевом индексе, прошлых эталонах в следующих элементах и нового отчета в конце списка

                    string description_3s = "3s - the current ref is actual, RemoveAll(r => !r.IsThisReportTheReference) will be executed";
                    bool resView_3s = ViewListOfReportsInConsole(constantsSet, description_3s, testScenario, reportsListOfTheScenario);

                    // здесь надо проверить весь список на наличие отчетов без признака эталона и все их надо удалить
                    reportsListOfTheScenario.RemoveAll(r => !r.IsThisReportTheReference);

                    // создаем и добавляем новый отчет с версией эталона в конец списка
                    TestReport theReportOfTheScenarioRefVer = CreateNewTestReport(testScenario, reportsListOfTheScenarioCount, false, maxVersion, testTimingReportStagesList, testReportHash);
                    reportsListOfTheScenario.Add(theReportOfTheScenarioRefVer);

                    Logs.Here().Information("theReportOfTheScenario was added with version {0}.", maxVersion);

                    string description_3e = "3e - the current ref is actual, we will write the current report to the end with the version of this ref";
                    bool resView_3e = ViewListOfReportsInConsole(constantsSet, description_3e, testScenario, reportsListOfTheScenario);

                    // здесь вызвать запись в ключ и закончить (return true - запись в ключ произведена)
                    await WriteTestScenarioReportsList(constantsSet, testScenario, reportsListOfTheScenario);
                    return true;
                }
                // здесь находимся, потому что эталон есть, но он устарел - новый отчет(ы) с ним не совпадает, надо узнавать насчет создания нового - так же return false
            }
            // здесь возвращаем, что надо продолжать
            return false;
        }

        // Search predicate returns true if a TestReport is without version and hash is the same as the sample.
        private static bool DownWithDissent(string testReportHash, TestReport theReportOfTheScenario)
        {
            bool reportsInPairAreEqual = String.Equals(testReportHash, theReportOfTheScenario.ThisReportHash);
            bool reportWOversion = theReportOfTheScenario.ThisReporVersion <= 0;
            return !reportsInPairAreEqual && reportWOversion;
        }

        private async Task<List<TestReport.TestReportStage>> TheReportsConfluenceForView(ConstantsSet constantsSet, int testScenario)
        {
            string eternalTestTimingStagesReportsLogKey = constantsSet.Prefix.IntegrationTestPrefix.EternalTestTimingStagesReportsLog.Value; // key-test-reports-timing-imprints-list

            List<TestReport> theScenarioReportsLast = await _cache.FetchHashedAsync<int, List<TestReport>>(eternalTestTimingStagesReportsLogKey, testScenario);

            List<TestReport.TestReportStage> testTimingReportStagesListCurrent = new();
            //Logs.Here().Information("United List - {@R}, Length = {0}.", new { TestTimingReportStages = testTimingReportStagesListCurrent }, testTimingReportStagesListCurrent.Count);

            for (int i = 0; i < theScenarioReportsLast.Count; i++)
            {
                testTimingReportStagesListCurrent.AddRange(theScenarioReportsLast[i].TestReportStages); // theScenarioReports.Count - 1
                Logs.Here().Information("Hash of hashes - {0} in theScenarioReports[{1}] .", theScenarioReportsLast[i].ThisReportHash, i);
            }

            //Logs.Here().Information("United List - {@R}, Length = {0}.", new { TestTimingReportStages = testTimingReportStagesListCurrent }, testTimingReportStagesListCurrent.Count);

            return testTimingReportStagesListCurrent;
        }

        // метод получает только константы, самостоятельно достает из них нужный ключ,
        // достает сохраненные шаги с засечками времени в словарь,
        // преобразовывает в список (через массив) и собирает хеши всех шагов для вычисления общего хеша отчета,
        // возвращает (отсортированный список) и хеш всего отчета
        // дополнительно тут подходящее место отсортировать по номеру цепочки, а зачем по номеру шага
        // 
        private async Task<(List<TestReport.TestReportStage>, string)> ConvertDictionaryWithReportToList(ConstantsSet constantsSet)
        {
            string currentTestReportKey = constantsSet.Prefix.IntegrationTestPrefix.CurrentTestReportKey.Value; // storage-key-for-current-test-report

            // нумерация ключей словаря начинается с 1 (так случилось)
            IDictionary<int, TestReport.TestReportStage> testTimingReportStages = await _cache.FetchHashedAllAsync<int, TestReport.TestReportStage>(currentTestReportKey);
            int testTimingReportStagesCount = testTimingReportStages.Count;

            //Logs.Here().Information("testTimingReportStages {@D} length {0}.", new { IDictionary = testTimingReportStages }, testTimingReportStagesCount);

            TestReport.TestReportStage[] testTimingReportStagesArray = new TestReport.TestReportStage[testTimingReportStagesCount];
            string[] stageHash = new string[testTimingReportStagesCount];

            // или <= testTimingReportStagesCount - индексы начинаются с 1
            for (int i = 1; i < testTimingReportStagesCount + 1; i++) //
            {
                // записать сумму хешей или хеш хешей в базовый класс
                // сдвигаем индексы к нулю
                stageHash[i - 1] = testTimingReportStages[i].StageReportHash;
                testTimingReportStagesArray[i - 1] = testTimingReportStages[i];
                //Logs.Here().Information("for i = {0} to end {1} - List = {2}, Array[i] = {3}, stageHash = {4}.", i, testTimingReportStagesCount, testTimingReportStages[i].StageReportFieldCounter, testTimingReportStagesArray[i-1].StageReportFieldCounter, stageHash[i-1]);
            }

            // будем сортировать массив хешей перед слиянием и вычислением общего хеша
            Array.Sort(stageHash);

            List<TestReport.TestReportStage> testTimingReportStagesList = testTimingReportStagesArray.OrderBy(x => x.ChainSerialNumber).ThenBy(x => x.StageReportFieldCounter).ToList();
            //Logs.Here().Information("testTimingReportStagesList {@D} length {0}.", new { List = testTimingReportStagesList }, testTimingReportStagesList.Count);
            // важно отсортировать до вычисления общего хеша
            string testReportForHash = String.Join("_", stageHash);
            string testReportHash = AuxiliaryUtilsService.CreateMD5(testReportForHash);

            return (testTimingReportStagesList, testReportHash);
        }

        private async Task<bool> WriteTestScenarioReportsList(ConstantsSet constantsSet, int testScenario, List<TestReport> theScenarioReports)
        {
            KeyType eternalTestTimingStagesReportsLog = constantsSet.Prefix.IntegrationTestPrefix.EternalTestTimingStagesReportsLog; // key-test-reports-timing-imprints-list

            await _cache.WriteHashedAsync<int, List<TestReport>>(eternalTestTimingStagesReportsLog.Value, testScenario, theScenarioReports, eternalTestTimingStagesReportsLog.LifeTime);

            return true;
        }

        // метод выводит таблицу с результатами текущего отчета о времени прохождения теста по контрольным точкам
        public async Task<bool> ViewComparedReportInConsole(ConstantsSet constantsSet, long tsTest99, int testScenario)//, List<TestReport.TestReportStage> testTimingReportStagesSource)
        {
            List<TestReport.TestReportStage> testTimingReportStagesSource = await TheReportsConfluenceForView(constantsSet, testScenario); // Fusion-Merge

            //List<TestReport.TestReportStage> testTimingReportStages = (from u in testTimingReportStagesSource
            //                                                           orderby u.StageReportFieldCounter
            //                                                           select u).ToList();

            List<TestReport.TestReportStage> testTimingReportStages = testTimingReportStagesSource.OrderBy(x => x.StageReportFieldCounter).ThenBy(y => y.ChainSerialNumber).ThenBy(z => z.TheScenarioReportsCount).ToList();

            // сначала перегнать все данные в печатный массив, определить максимумы,
            // сделать нормировку масштабирования для заданной ширины печати тайм - лайна
            // привязать названия и значения к свободным местам тайм-лайн
            // попробовать выводить в две строки - одна данные, вторая тайм-лайн
            // событие таймера выделять отдельной строкой и начинать отсчет времени заново

            // изменить названия точек методов на номера точек по порядку - можно синтезировать при печати имяМетода-1
            // сделать сначала текстовую таблицу, без тайм - лайн
            // для тайм-лайн надо синтезировать времена выполнения всего метода с данным номером задачи - начало, важный узел и конец метода
            // тогда будет понятно, зачем нужен тайм - лайн

            //char ttt = '\u2588'; // █ \u2588 ▮ U+25AE ▯ U+25AF 
            //int timeScaling = 5;

            int screenFullWidthLinesCount = 228;
            char screenFullWidthTopLineChar = '-';
            char screenLineChar1C79 = '\u1C79'; // Ol Chiki Gaahlaa Ttuddaag --> ᱹ

            // проверить наличие ключа
            // проверить наличие словаря
            // проверить, что словарь не нулевой

            //IDictionary<int, TestReport.TestReportStage> testTimingReportStages = await _cache.FetchHashedAllAsync<int, TestReport.TestReportStage>(currentTestReportKey);
            int testTimingReportStagesCount = testTimingReportStages.Count;

            TestReport.TestReportStage stage1 = testTimingReportStages[1];
            int r103 = stage1.TheScenarioReportsCount;
            TestReport.TestReportStage stageLast = testTimingReportStages[testTimingReportStagesCount - 2];
            int rL05 = (int)stageLast.TsTest;

            Console.WriteLine($"\n  Timing imprint report on testScenario No: {testScenario,-3:d} | total stages in the report = {testTimingReportStagesCount,-4:d} | total test time = {(int)tsTest99,5:d} msec."); // \t

            Console.WriteLine(("").PadRight(screenFullWidthLinesCount, screenFullWidthTopLineChar));
            Console.WriteLine("|{0,5}|{1,5}|{2,5}| {3,-37} | {4,8} | {5,8} | {6,8} | {7,5} | {8,8} | {9,-40} | {10,-33} |", "stage", "chain", "index", "CallingMethod-PointNum/CallingNum", "timePrev", "timeWork", "timeDlt", "W-int", "W-bool", "WorkActionName", "StageReportHash");
            Console.WriteLine(("").PadRight(screenFullWidthLinesCount, screenFullWidthTopLineChar));

            int r01Prev = 1;
            int r04prev = 0;

            for (int i = 0; i < testTimingReportStagesCount; i++) //
            {
                //if (i > 1)
                //{
                //    TestReport.TestReportStage stagePrev = testTimingReportStages[i - 1];
                //    r04prev = (int)stagePrev.TsWork;
                //}

                TestReport.TestReportStage stage = testTimingReportStages[i];
                int r01 = stage.StageReportFieldCounter;
                int r02 = stage.ChainSerialNumber;
                int r03 = stage.TheScenarioReportsCount;
                int r04 = (int)stage.TsWork;
                int r05 = (int)stage.TsTest;
                string r06 = stage.MethodNameWhichCalled;
                int r07 = stage.WorkActionNum;
                bool r08 = stage.WorkActionVal;

                static string r07Cor(int r07) => r07 < 0 ? " (N/A)" : $"- {r07}";
                //string GetWeatherDisplay(double tempInCelsius) => tempInCelsius < 20.0 ? "Cold." : "Perfect!";

                string r09 = $"{stage.WorkActionName} {r07Cor(r07)} ({r08})";
                int r10 = stage.ControlPointNum;
                int r11 = stage.CallingCountOfWorkMethod;
                int r12 = stage.CallingCountOfThisMethod;
                string r13 = stage.StageReportHash;

                string r06Num = $"{r06}-{r10} / {r11}";
                int r04delta = 0; // r04 - r04prev;

                if (r01 > r01Prev)
                {
                    Console.WriteLine(("").PadRight(180, screenLineChar1C79)); //screenFullWidthLinesCount
                }
                r01Prev = r01;

                Console.WriteLine("| {0,3:d} | {1,3:d} | {2,3:d} | {3,-37} | {4,8:d} | {5,8:d} | {6,8:d} | {7,5:d} | {8,8:b} | {9,-40} | {10,33} |", r01, r02, r03, r06Num, r04prev, r04, r04delta, r07, r08, new string(r09.Take(40).ToArray()), r13);
            }
            Console.WriteLine(("").PadRight(screenFullWidthLinesCount, '*'));

            return true;
        }

        // метод выводит таблицу списка отчетов 
        private bool ViewListOfReportsInConsole(ConstantsSet constantsSet, string description, int testScenario, List<TestReport> reportsListOfTheScenario)
        {
            //char ttt = '\u2588'; // █ \u2588 ▮ U+25AE ▯ U+25AF 

            int reportsListOfTheScenarioCount = reportsListOfTheScenario.Count;

            int screenFullWidthLinesCount = 228;
            char screenFullWidthTopLineChar = '-';
            char screenFullWidthBetweenLineChar = '\u1C79'; // Ol Chiki Gaahlaa Ttuddaag --> ᱹ

            Console.WriteLine($"\n  Timing imprint report List on testScenario No: {testScenario,-3:d} | List report count = {reportsListOfTheScenario.Count,-4:d} | {description}."); // \t

            Console.WriteLine(("").PadRight(screenFullWidthLinesCount, screenFullWidthTopLineChar));
            Console.WriteLine("| {0,5} | {1,5} | {2,5} | {3,5} | {4,37} | {5,6} | {6,5} |", "scnrm", "index", "ver.N", "isRef", "this report hash", "stages", "i");
            Console.WriteLine(("").PadRight(screenFullWidthLinesCount, screenFullWidthTopLineChar));

            for (int i = 0; i < reportsListOfTheScenarioCount; i++) //
            {
                TestReport stage = reportsListOfTheScenario[i];
                int r01 = stage.TestScenarioNum;
                int r02 = stage.TheScenarioReportsCount;
                int r03 = stage.ThisReporVersion;
                bool r04 = stage.IsThisReportTheReference;
                string r05 = stage.ThisReportHash;
                int r06 = stage.TestReportStages.Count;

                Console.WriteLine("| {0,5:d} | {1,5:d} | {2,5:d} | {3,5} | {4,37} | {5,6:d} | {6,5:d} |", r01, r02, r03, r04, r05, r06, i);
                Console.WriteLine(("").PadRight(screenFullWidthLinesCount, screenFullWidthBetweenLineChar));
            }

            return true;
        }





        //// 
        //private List<TestReport> FindIdenticalReportsCount(int reportsCountToStartComparison, List<TestReport> reportsListOfTheScenario, TestReport theScenarioReportRef, int equalReportsCount)
        //{
        //    // сравниваем количество одинаковых отчетов с константой и если равно (больше вроде бы не может быть),
        //    // то сохраняем эталонный отчет в нулевой индекс
        //    // предварительно надо проверить, что там сейчас - и, если эталон с другой (меньшей) версией,
        //    // то вытолкнуть (вставить) его в первый индекс (не затереть первый)

        //    Logs.Here().Information("Ref to insert {@F}.", new { ReportRef = theScenarioReportRef });

        //    if (equalReportsCount >= reportsCountToStartComparison)
        //    {
        //        Logs.Here().Information("Source {@R} length {0}.", new { ReportsList = reportsListOfTheScenario }, reportsListOfTheScenario.Count);

        //        reportsListOfTheScenario.RemoveAt(0);

        //        Logs.Here().Information("Removed 0 {@R} length {0}.", new { ReportsList = reportsListOfTheScenario }, reportsListOfTheScenario.Count);

        //        reportsListOfTheScenario.Insert(0, theScenarioReportRef);

        //        Logs.Here().Information("Inserted Ref at 0 {@R} length {0}.", new { ReportsList = reportsListOfTheScenario }, reportsListOfTheScenario.Count);

        //    }

        //    return reportsListOfTheScenario;
        //}

        //private int ExistingReportsComparison(int reportsCountToStartComparison, List<TestReport> ReportsListOfTheScenario, string testReportHash, int reportsWOversionsCount)
        //{
        //    // прежде чем записывать новый отчет в список, надо узнать, не требуется ли сравнение            
        //    int theScenarioReportsCount = ReportsListOfTheScenario.Count;
        //    int equalReportsCount = 0;

        //    // сравниваем количество отчетов без версии, полученное в начале теста с константой
        //    // если оно больше, то начинаем цикл сравнения хешей отчетов, чтобы понять, сколько их набралось одинаковых
        //    if (reportsWOversionsCount >= reportsCountToStartComparison)
        //    {
        //        for (int i = theScenarioReportsCount - 1; i > 0; i--)
        //        {
        //            bool reportsInPairAreEqual = String.Equals(testReportHash, ReportsListOfTheScenario[i].ThisReportHash);

        //            if (reportsInPairAreEqual)
        //            {
        //                equalReportsCount++;
        //            }
        //            else
        //            {
        //                // если встретился отчет с другим хешем, сразу можно выйти из цикла с проверкой - потом оформить в отдельный метод
        //                // но надо посмотреть на счетчик - если нет даже двух одинаковых, то выходить совсем, иначе - уже описано
        //                // return
        //            }
        //        }
        //    }

        //    return equalReportsCount;
        //}

        //// метод сравнивает предыдущие отчеты с текущим (в виде его хеша)
        //// и на выходе возвращает инструкции, что делать дальше, варианта два (уже три) -
        //// 1 - bool false, int 0 - сформировать и записать текущий отчет в конец списка
        //// 2 - bool true, int any - сформировать из текущего отчета эталонный, дать ему версию +1 от int, записать его в нулевой индекс,
        //// присвоить текущему обычному отчету ту же версию, что и эталонному и записать его в конец списка
        //// 3 - bool false, int > 0 - эталон не создавать, присвоить текущему обычному отчету версию int и записать его в конец списка 
        //// внутри себя метод удалит лишние отчеты из списка при необходимости
        //private List<TestReport> ReportsAnalysisForReferenceAssigning(ConstantsSet constantsSet, int testScenario, List<TestReport> reportsListOfTheScenario, List<TestReport.TestReportStage> testTimingReportStagesList, string testReportHash)
        //{
        //    // 3/5 - взять из констант, назвать типа количество отчетов для начала проведения сравнения - ReportsCountToStartComparison
        //    int reportsCountToStartComparison = 3;
        //    // достаточное количество последовательных совпадающих отчетов, чтобы удалить дальнейшие несовпадающие
        //    int equalReportsCountToRemoveDifferents = 2;
        //    // флаг (признак), является ли этот отчет действующим эталоном
        //    // нужен ли признак, что он был эталоном или это и так будет понятно по номеру версии?
        //    bool isThisReportTheReference = false;
        //    // выходное значение метода - надо ли этот отчет сделать эталонным
        //    bool thisReportMustBecomeReference = false;
        //    // счетчик количества отчетов без версий (с нулевой версией) - для определения готовности к спариванию
        //    int reportsWOversionsCount = 0;
        //    // счетчик одинаковых отчетов, чтобы определить, что пришло время создавать эталон
        //    int equalReportsCount = 0;
        //    bool wereConsistentlyEqual = true;
        //    int maxVersion = 0;
        //    // если в списке уже что-то есть, кроме добавленной пустышки в пустой, изучаем элементы списка
        //    int reportsListOfTheScenarioCount = reportsListOfTheScenario.Count;
        //    if (reportsListOfTheScenarioCount > 1)
        //    {
        //        reportsListOfTheScenario = CheckReferenceToAssignVersion(constantsSet, testScenario, reportsListOfTheScenario, testTimingReportStagesList, testReportHash);
        //        int reportsListOfTheScenarioCount1 = reportsListOfTheScenario.Count;

        //        // а здесь находимся, потому что эталона нет, надо узнавать насчет его создания 
        //        // ситуации одинаковые и будут отличаться на выходе только значением версии -
        //        // 0, если эталона не было и больше нуля, если эталон устарел

        //        for (int i = reportsListOfTheScenarioCount - 1; i > 0; i--)
        //        {
        //            // проверяем очередной отчет из списка на наличие версии, если ее нет, идем внутрь
        //            if (reportsListOfTheScenario[i].ThisReporVersion <= 0)
        //            {
        //                // увеличиваем счетчик отчетов без версий (типа, свежих и несравненных)
        //                reportsWOversionsCount++;

        //                // сравниваем текущий хеш с проверяемым отчетом, если хеши одинаковые, увеличиваем счетчик совпадающих отчетов
        //                // тут еще надо бы искать совпадающие подряд, а если раз не совпало, дальше уже не искать
        //                bool reportsInPairAreEqual = String.Equals(testReportHash, reportsListOfTheScenario[i].ThisReportHash);
        //                if (reportsInPairAreEqual && wereConsistentlyEqual)
        //                {
        //                    equalReportsCount++;
        //                }
        //                else
        //                {
        //                    wereConsistentlyEqual = false;
        //                }
        //            }
        //        }

        //        // рассмотрим варианты - 
        //        // 1 reportsWOversionsCount < reportsCountToStartComparison - просто добавляем отчет без версии
        //        // 2 reportsWOversionsCount >=, equalReportsCount < reportsCountToStartComparison - проверяем equalReportsCount >= - equalReportsCountToStartRefAssigning - удаляем все без версий, но несовпадающие с двумя (или больше?) последними
        //        // тут еще надо бы искать совпадающие подряд, а если раз не совпало, дальше уже не искать
        //        // 3 оба больше (равны на самом деле) - можно создавать эталон
        //        // 

        //        // отчетов без версий достаточное количество для обработки (сравнение хешей)
        //        if (reportsWOversionsCount >= reportsCountToStartComparison)
        //        {
        //            // одинаковых отчетов достаточно для создания эталона
        //            if (equalReportsCount >= reportsCountToStartComparison)
        //            {
        //                // здесь надо создать эталонный отчет и увеличить текущую версию на 1 для дальнейшего применения
        //                maxVersion++;
        //                TestReport theScenarioReportRef = CreateNewTestReport(testScenario, 0, true, maxVersion, testTimingReportStagesList, testReportHash);
        //                //Logs.Here().Information("Source {@R} length {0}.", new { ReportsList = reportsListOfTheScenario }, reportsListOfTheScenario.Count);
        //                reportsListOfTheScenario.RemoveAt(0);
        //                //Logs.Here().Information("Removed 0 {@R} length {0}.", new { ReportsList = reportsListOfTheScenario }, reportsListOfTheScenario.Count);
        //                reportsListOfTheScenario.Insert(0, theScenarioReportRef);
        //                //Logs.Here().Information("Inserted Ref at 0 {@R} length {0}.", new { ReportsList = reportsListOfTheScenario }, reportsListOfTheScenario.Count);

        //                // текущий отчет записать в конец с версией нового эталона
        //                TestReport theReportOfTheScenarioVerNewRef = CreateNewTestReport(testScenario, reportsListOfTheScenarioCount, false, maxVersion, testTimingReportStagesList, testReportHash);
        //                reportsListOfTheScenario.Add(theReportOfTheScenarioVerNewRef);

        //                Logs.Here().Information("theReportOfTheScenario was added with version {0}.", maxVersion);

        //                string description4 = "here we need to create a reference report and increase the current version by 1 for further use";
        //                bool resView4 = ViewListOfReportsInConsole(constantsSet, description4, testScenario, reportsListOfTheScenario);

        //                return reportsListOfTheScenario;
        //            }
        //            // одинаковых отчетов еще мало для захвата власти, но уже достаточно для подавления несогласных
        //            if (equalReportsCount >= equalReportsCountToRemoveDifferents)
        //            {
        //                // удаляем всех без версий, но несовпадающие с equalReportsCountToStartRefAssigning последними
        //                reportsListOfTheScenario.RemoveAll(r => r.ThisReporVersion <= 0 && !String.Equals(testReportHash, r.ThisReportHash));
        //            }
        //        }
        //        // отчетов без версий недостаточное количество для обработки (проведения сравнения хешей) и, возможно, по дороге удалили всех несогласных
        //        // добавляем новый отчет в конец списка без версии
        //        TestReport theReportOfTheScenario = CreateNewTestReport(testScenario, reportsListOfTheScenarioCount, false, -1, testTimingReportStagesList, testReportHash);
        //        reportsListOfTheScenario.Add(theReportOfTheScenario);
        //    }

        //    string description5 = "add a new report to the end of the list without version";
        //    bool resView5 = ViewListOfReportsInConsole(constantsSet, description5, testScenario, reportsListOfTheScenario);

        //    return reportsListOfTheScenario;
        //}

























        // первый параметр - isRequestForTestStopWatch = true, Work - false
        // второй параметр - запустить/прочитать = true, остановить/сбросить = false
        // возвращается засечка времени в мсек, без остановки секундомера
        //public long StopwatchesControlAndRead(bool isRequestedStopWatchTest, bool control)
        //{
        //    Stopwatch stopWatch;
        //    if (isRequestedStopWatchTest)
        //    {
        //        stopWatch = _stopWatchTest;
        //    }
        //    else
        //    {
        //        stopWatch = _stopWatchWork;

        //    }
        //    // надо проверять текущее состояние секундомера перед его изменением
        //    bool currentState = stopWatch.IsRunning;

        //    string stopWatchState;

        //    //// если надо запустить и он остановлен (запускаем)
        //    //if (control && !currentState)
        //    //{
        //    //    _stopWatchTest.Start();
        //    //    stopWatchState = "was started";
        //    //}
        //    //// если надо остановить и он запущен (останавливаем)
        //    //if (!control && currentState)
        //    //{
        //    //    _stopWatchTest.Stop();
        //    //    stopWatchState = "was stopped";
        //    //}
        //    //// если надо запустить и он уже запущен (показываем текущее время)
        //    //if (control && currentState)
        //    //{
        //    //    stopWatchState = "has beed started already";
        //    //}
        //    //// если надо остановить и он уже остановлен (сбрасываем)
        //    //if (!control && !currentState)
        //    //{
        //    //    stopWatchState = "had been already stopped and was just reset");
        //    //    _stopWatchTest.Reset();
        //    //}

        //    // требуется запустить секундомер - прислали true
        //    if (control)
        //    {
        //        // если надо запустить и он уже запущен (показываем текущее время)
        //        if (currentState)
        //        {
        //            stopWatchState = "has beed started already";
        //        }
        //        // если надо запустить и он остановлен (запускаем)
        //        else
        //        {
        //            //stopWatch = Stopwatch.StartNew();
        //            stopWatch.Start();
        //            stopWatchState = "has been started";
        //        }
        //    }
        //    // требуется остановить секундомер - прислали false
        //    else
        //    {
        //        // если надо остановить и он запущен (останавливаем)
        //        if (currentState)
        //        {
        //            stopWatch.Stop();
        //            stopWatchState = "has been stopped";
        //        }
        //        // если надо остановить и он уже остановлен (сбрасываем)
        //        else
        //        {
        //            stopWatch.Reset();
        //            stopWatchState = "had been already stopped and was just reset";
        //        }
        //    }

        //    //TimeSpan tsControl = stopWatch.Elapsed;
        //    long stopwatchMeasuredTime = stopWatch.ElapsedMilliseconds; // double Elapsed.TotalMilliseconds
        //    Logs.Here().Debug("Stopwatch {0} {1}. It shows {2} msec.", nameof(stopWatch), stopWatchState, stopwatchMeasuredTime);

        //    return stopwatchMeasuredTime;
        //}

    }
}
