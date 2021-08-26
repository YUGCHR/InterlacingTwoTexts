using System;
using System.Collections.Generic;
//using System.Text.Json;
using Newtonsoft.Json;

namespace Shared.Library.Models
{
    public class TestReport
    {
        // фактически это id для списка List<TestReport> и он же номер поля в ключе вечного лога
        [JsonProperty(PropertyName = "testScenarioNum")]
        public int TestScenarioNum { get; set; }

        // id элемента списка List<TestReport>
        // фактически это id для списка List<TestActionsReport>
        [JsonProperty(PropertyName = "guid")]
        public string Guid { get; set; }

        // оно же, только int
        [JsonProperty(PropertyName = "testId")]
        public int TestId { get; set; }

        // список всех шагов в отчёте о тесте
        [JsonProperty(PropertyName = "testReportStages")]
        public List<TestReportStage> TestReportStages { get; set; }

        public class TestReportStage
        {
            // StageId - номер шага с записью отметки времени теста, он же номер поля в ключе записи текущего отчёта
            [JsonProperty(PropertyName = "stageReportFieldCounter")]
            public int StageReportFieldCounter { get; set; }

            // серийный номер единичной цепочки теста - обработка одной книги от события From до создания ключа кафе
            [JsonProperty(PropertyName = "chainSerialNumber")]
            public int ChainSerialNumber { get; set; }

            // Current Test Serial Number for this Scenario - номер теста в пакете тестов по данному сценарию, он же индекс в списке отчётов
            [JsonProperty(PropertyName = "theScenarioReportsCount")]
            public int TheScenarioReportsCount { get; set; } // -- ??

            // отметка времени от старта рабочей цепочки
            [JsonProperty(PropertyName = "tsWork")]
            public long TsWork { get; set; }

            // отметка времени от начала теста
            [JsonProperty(PropertyName = "tsTest")]
            public long TsTest { get; set; }

            // имя вызвавшего метода NameOfTheCallingMethod
            [JsonProperty(PropertyName = "methodNameWhichCalled")]
            public string MethodNameWhichCalled { get; set; }

            // ключевое слово, которым делится вызвавший метод - что-то о его занятиях
            [JsonProperty(PropertyName = "workActionName")]
            public string WorkActionName { get; set; }

            // количество одновременных вызовов этого метода
            [JsonProperty(PropertyName = "callingNumOfAddStageToTestTaskProgressReport")]
            public int CallingNumOfAddStageToTestTaskProgressReport { get; set; }
        }
    }
}
