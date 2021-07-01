using Microsoft.Extensions.Configuration;
using Shared.Library.Models;

namespace ConstantData.Services
{
    public interface IConstantsCollectionService
    {
        public ConstantsSet SettingConstants { get; set; }        
    }

    // Service to load constants from appsettings.json to model ConstantsSet
    public class ConstantsCollectionService : IConstantsCollectionService
    {
        public ConstantsCollectionService(IConfiguration configuration)
        {
            SettingConstants = new ConstantsSet();
            // структура appsettings и класса ConstantsSet должны совпадать
            configuration.GetSection("SettingConstants").Bind(SettingConstants);
        }
        
        public ConstantsSet SettingConstants { get; set; }
    }
}
