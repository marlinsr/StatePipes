using StatePipes.Common;
using StatePipes.Interfaces;
using StatePipes.ProcessLevelServices.Internal;

namespace StatePipes.ProcessLevelServices
{
    public class ConfigurationSettingsHelper
    {
        private static readonly Dictionary<string, object> ConfigSettingsDictionary = [];
        public static T Instance<T>(bool useDefaults = false) where T : class, IConfigSettings, new()
        {
            lock (ConfigSettingsDictionary)
            {
                var instance = (T)new T().GetDefaults();
                var typeFullName = typeof(T).FullName;
                if (string.IsNullOrEmpty(typeFullName)) throw new ArgumentException("Type does not have full name!");
                if (ConfigSettingsDictionary.TryGetValue(typeFullName, out object? val)) return JsonUtility.Clone((T)val);
                if (!useDefaults) instance = JsonFileHelperUtility.ReadFile<T>() ?? instance;
                Save(instance);
                return JsonUtility.Clone(instance);
            }
        }
        public static void Save<T>(T obj) where T : class, IConfigSettings, new()
        {
            lock (ConfigSettingsDictionary)
            {
                var typeFullName = typeof(T).FullName;
                if (string.IsNullOrEmpty(typeFullName)) throw new ArgumentException("Type does not have full name!");
                T instance = JsonUtility.Clone(obj);
                JsonFileHelperUtility.SaveFile(instance);
                ConfigSettingsDictionary[typeFullName] = instance;
            }
        }
    }
}
