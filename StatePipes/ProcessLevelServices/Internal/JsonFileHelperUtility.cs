using StatePipes.Common;

namespace StatePipes.ProcessLevelServices.Internal
{
    internal class JsonFileHelperUtility
    {
        public static T? ReadFile<T>(string filename) where T : class
        {
            var path = GetPathForFileInConfigDirectory(filename);
            if (!File.Exists(path)) return default;
            string jsonString = File.ReadAllText(path);
            T? ret = JsonUtility.GetObjectForJsonString<T>(jsonString);
            if (ret != null) return ret;
            return default;
        }
        public static T? ReadFile<T>() where T : class => ReadFile<T>($"{typeof(T).Name}.json");
        public static void SaveFile<T>(string filename, T obj) where T : class
        {
            var path = GetPathForFileInConfigDirectory(filename);
            string jsonString = JsonUtility.GetJsonStringForObject(obj);
            CreateConfigDirectory();
            File.WriteAllText(path, jsonString);
        }
        public static void SaveFile<T>(T obj) where T : class => SaveFile($"{typeof(T).Name}.json", obj);
        public static string GetPathForFileInConfigDirectory(string filename) => Path.Combine(DirHelper.GetProductDataCategoryDirectoryForProcess(DirHelper.FileCategory.Config), filename);
        public static void CreateConfigDirectory()
        {
            var configDirectory = DirHelper.GetProductDataCategoryDirectoryForProcess(DirHelper.FileCategory.Config);
            if (!Directory.Exists(configDirectory)) Directory.CreateDirectory(configDirectory);
        }
    }
}
