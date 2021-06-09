using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
//using Newtonsoft.Json;

namespace Shared.Library.Models
{
    public class TaskDescriptionAndProgress
    {
        //[JsonProperty(PropertyName = "tasksCountInPackage")]
        public int TasksCountInPackage { get; set; }

        //[JsonProperty(PropertyName = "tasksPackageGuid")]
        public string TasksPackageGuid { get; set; }

        //[JsonProperty(PropertyName = "backServerPrefixGuid")]
        public string BackServerPrefixGuid { get; set; }

        //[JsonProperty(PropertyName = "taskDescription")]
        public TaskComplicatedDescription TaskDescription { get; set; }

        //[JsonProperty(PropertyName = "taskState")]
        public TaskProgressState TaskState { get; set; }

        public class TaskComplicatedDescription
        {
            //[JsonProperty(PropertyName = "taskGuid")]
            public string TaskGuid { get; set; }

            //[JsonProperty(PropertyName = "cycleCount")]
            public int CycleCount { get; set; }
            
            //[JsonProperty(PropertyName = "taskDelayTimeFromSeconds")]
            public int TaskDelayTimeFromMilliSeconds { get; set; }
        }

        public class TaskProgressState
        {
            //[JsonProperty(PropertyName = "isTaskRunning")]
            public bool IsTaskRunning { get; set; }
            
            //[JsonProperty(PropertyName = "isTaskRunning")]
            public int TaskCompletedOnPercent  { get; set; }
        }

        public class SingleTaskOverview
        {
            //[JsonProperty(PropertyName = "taskGuid")]
            public string TaskGuid { get; set; }

            //[JsonProperty(PropertyName = "taskDescription")]
            public TaskComplicatedDescription TaskDescription { get; set; }

            //[JsonProperty(PropertyName = "taskState")]
            public TaskProgressState TaskState { get; set; }
        }
    }

    // заготовка для хранения всех задач пакета
    public class TaskPackageDescriptionAndProgress
    {
        //[JsonProperty(PropertyName = "tasksCountInPackage")]
        public int TasksCountInPackage { get; set; }

        //[JsonProperty(PropertyName = "tasksPackageGuid")]
        public string TasksPackageGuid { get; set; }

        //[JsonProperty(PropertyName = "backServerPrefixGuid")]
        public string BackServerPrefixGuid { get; set; }

        // заготовка для хранения всех задач пакета

        //[JsonProperty(PropertyName = "singleTaskOverview")]
        public Dictionary<string, SingleTaskOverview> SingleTasksOverview { get; set; }

        public class SingleTaskOverview
        {
            //[JsonProperty(PropertyName = "taskGuid")]
            public string TaskGuid { get; set; }

            //[JsonProperty(PropertyName = "taskDescription")]
            public TaskDescriptionAndProgress.TaskComplicatedDescription TaskDescription { get; set; }

            //[JsonProperty(PropertyName = "taskState")]
            public TaskDescriptionAndProgress.TaskProgressState TaskState { get; set; }
        }
    }
}
