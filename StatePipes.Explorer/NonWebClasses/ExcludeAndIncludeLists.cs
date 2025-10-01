using StatePipes.Common;

namespace StatePipes.Explorer.NonWebClasses
{
    public class ExcludeAndIncludeLists
    {
        public List<string> Include { get; set; } = new List<string>();
        public List<string> Exclude { get; set; } = new List<string>();

        public const string ExcludeAllString = "*ALL*";
        public ExcludeAndIncludeLists Clone()
        {
            return GetFromJson(GetJsonString());
        }
        public string GetJsonString()
        {
            string ret = JsonUtility.GetJsonStringForObject(this, true);
            ret = ret.Replace("\"", "%22").Replace(",","%2C").Replace("{","").Replace("}", "");
            return ret;
        }
        public static ExcludeAndIncludeLists GetFromJson(string json)
        {
            json = "{" + json.Replace("%22", "\"").Replace("%2C",",") + "}";
            return JsonUtility.GetObjectForJsonString<ExcludeAndIncludeLists>(json) ?? new ExcludeAndIncludeLists();
        }
        public static ExcludeAndIncludeLists GetFromCsvs(string includeList, string excludeList)
        {
            ExcludeAndIncludeLists excludeIncludeLists = new ExcludeAndIncludeLists();
            if(!string.IsNullOrEmpty(includeList))
            {
                includeList = includeList.Trim();
                var arr = includeList.Split(',');
                arr.ToList().ForEach(item => { if (!string.IsNullOrEmpty(item.Trim())) excludeIncludeLists.Include.Add(item); });
            }
            if (!string.IsNullOrEmpty(excludeList))
            {
                excludeList = excludeList.Trim();
                var arr = excludeList.Split(',');
                arr.ToList().ForEach(item => { if (!string.IsNullOrEmpty(item.Trim())) excludeIncludeLists.Exclude.Add(item); });
            }
            return excludeIncludeLists;
        }
        public bool IsIncluded(string fullName)
        {
            return !Exclude.Any()
                || Include.Where(t => fullName.StartsWith(t)).Any()
                || (!Exclude.Where(t => t == ExcludeAndIncludeLists.ExcludeAllString).Any() && !Exclude
                .Where(t => fullName.StartsWith(t)).Any());
        }
    }
}
