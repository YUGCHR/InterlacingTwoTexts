using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Library.Models
{
    public class JsonTestModel
    {
        [JsonProperty(PropertyName = "default")]
        public string Default { set; get; }

        [JsonProperty(PropertyName = "microsoft")]
        public string Microsoft { set; get; }

        [JsonProperty(PropertyName = "lifetime")]
        public string Lifetime { set; get; }
    }
}
