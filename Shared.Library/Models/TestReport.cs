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
            [JsonProperty(PropertyName = "stageId")]
            public int StageId { get; set; }

            // по сути, такой же StageId
            [JsonProperty(PropertyName = "stageReportFieldCounter")]
            public int StageReportFieldCounter { get; set; }

            [JsonProperty(PropertyName = "theScenarioReportsCount")]
            public int TheScenarioReportsCount { get; set; } // -- ??

            [JsonProperty(PropertyName = "ts")]
            public TimeSpan Ts { get; set; }

            [JsonProperty(PropertyName = "workActionName")]
            public string WorkActionName { get; set; }
        }
    }
}
