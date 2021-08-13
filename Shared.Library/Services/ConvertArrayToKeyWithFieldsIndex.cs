using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shared.Library.Models;

namespace Shared.Library.Services
{
    public interface IConvertArrayToKeyWithIndexFields
    {
        public Task<(string, int)> CreateTestScenarioKey(ConstantsSet constantsSet, int testScenario);
    }

    public class ConvertArrayToKeyWithIndexFields : IConvertArrayToKeyWithIndexFields
    {
        private readonly IAuxiliaryUtilsService _aux;
        private readonly ICacheManagerService _cache;

        public ConvertArrayToKeyWithIndexFields(
            IAuxiliaryUtilsService aux,
            ICacheManagerService cache)
        {
            _aux = aux;
            _cache = cache;
        }

        private static Serilog.ILogger Logs => Serilog.Log.ForContext<ConvertArrayToKeyWithIndexFields>();

        // метод создаёт из последовательности команд в List<int> (пришедшим из веб-интерфейса)
        // ключ с полями-индексами порядка команд и нужной активностью в значениях
        public async Task<(string, int)> CreateTestScenarioKey(ConstantsSet constantsSet, int testScenario)
        {
            string testScenarioSequenceKey = constantsSet.Prefix.IntegrationTestPrefix.TestScenarioSequenceKey.Value; // test-scenario-sequence
            double testScenarioSequenceKeyLifeTime = constantsSet.Prefix.IntegrationTestPrefix.TestScenarioSequenceKey.LifeTime; // 0.001

            // Scenario 1
            int[] scenario1 = new int[] { 121, 221, -3700 };

            // Scenario 2
            int[] scenario2 = new int[] { 121, 221, -4500, 321 };

            // Scenario 3
            int[] scenario3 = new int[] { 121, 221, -4700, 321, -1000, 122, 222, -4800, 322, -1000, 123, 223, -4900, 323, -1000 };

            int[] selectedScenario = SwitchArraySelect(testScenario, scenario1, scenario2, scenario3);
            Logs.Here().Information("Scenario {0} was selected - {@S}", testScenario, new { ScenarioSequence = selectedScenario });

            bool testSettingKey1WasDeleted = await _aux.RemoveWorkKeyOnStart(testScenarioSequenceKey);

            IDictionary<int, int> fieldValues = new Dictionary<int, int>();

            for (int i = 0; i < selectedScenario.Length; i++)
            {
                fieldValues.Add(i, selectedScenario[i]);
            }

            await _cache.WriteHashedAsync<int, int>(testScenarioSequenceKey, fieldValues, testScenarioSequenceKeyLifeTime);

            IDictionary<int, int> testScenarioSequenceStepsValues = await _cache.FetchHashedAllAsync<int, int>(testScenarioSequenceKey);

            foreach (var p in testScenarioSequenceStepsValues)
            {
                (int i, int v) = p;
                if (v != selectedScenario[i])
                {
                    Logs.Here().Error("Scenario creation was failed - {0} != {1}", v, selectedScenario[i]);
                    return (null, 0);
                }
            }
            // на самом деле возвращать нечего и некому, так как метод будет в ControllerDataManager и общаться с сервером только через ключ
            return (testScenarioSequenceKey, selectedScenario.Length);
        }

        private static int[] SwitchArraySelect(int testScenario, int[] scenario1, int[] scenario2, int[] scenario3) => testScenario switch
        {
            1 => scenario1,
            2 => scenario2,
            3 => scenario3,
            _ => throw new ArgumentOutOfRangeException(nameof(testScenario), $"Not expected direction value: {testScenario}"),
        };

    }
}
