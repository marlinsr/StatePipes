using System.Runtime.Loader;
using System.Text;

namespace StatePipes.ServiceCreatorTool
{
    internal class ProxyGeneratorCommon
    {
        public ReferencedAssemblies Assemblies { get; private set; }
        public List<Type> Types { get; private set; }
        public TypeSerializationList TypeSerializations { get; private set; }
        public string StateMachineFullName { get; private set; }
        public string stateMachineNamespace { get; private set; }
        public PathHelper PathProvider { get; private set; }
        public string CodeGenerationBaseNamespace { get; private set; }
        public string ProxyMoniker { get; private set; }
        public StringBuilder CodeGenerationString { get;} = new StringBuilder();
        public NamespaceCollection NamespaceList { get; } = new NamespaceCollection();
        public string CodeGenerationNamespace { get; set; } = string.Empty;
        public Dictionary<string, Dictionary<string, string>> ValueObjectContructorParametersDictionary;
        public string DefaultConfigNamespace { get; private set; }

        public ProxyGeneratorCommon(string fullPathFileName, string codeGenerationBaseNamespace, string proxyMoniker, PathHelper pathProvider)
        {
            ValueObjectContructorParametersDictionary = new();
            PathProvider = pathProvider;
            CodeGenerationBaseNamespace = codeGenerationBaseNamespace;
            ProxyMoniker = proxyMoniker;
            Assemblies = new ReferencedAssemblies(fullPathFileName);
            if (Assemblies.CommandType is null || Assemblies.EventType is null) throw new Exception("Command or Event type not found in statepipes!");
            var stateMachineType = GetStateMachineName(codeGenerationBaseNamespace);
            if (stateMachineType == null) throw new Exception("stateMachineType is null");
            StateMachineFullName = stateMachineType.FullName!;
            stateMachineNamespace = stateMachineType.Namespace!;
            var targetAssembly = Assemblies.GetTargetAssembly();
            if (targetAssembly == null) throw new Exception("Target assembly not found");
            var defaultServiceConfiguration = targetAssembly.GetTypesNoExceptions().FirstOrDefault(t => t.Name == "DefaultServiceConfiguration");
            DefaultConfigNamespace = defaultServiceConfiguration?.Namespace ?? "Unable To Find DefaultServiceConfiguration";
            TypeSerializations = new TypeSerializationList();
            Types = targetAssembly.GetTypesNoExceptions().Where(t => t.IsPublic && !t.IsAbstract && !t.IsGenericType &&
                   (Assemblies.CommandType.IsAssignableFrom(t) || Assemblies.EventType.IsAssignableFrom(t))).ToList();
        }
        private Type? GetStateMachineName(string projectName)
        {
            List<Type> stateMachineOptions = new List<Type>();
            try
            {
                var assemblyFilePath = Path.Combine(PathProvider.GetPath(PathName.Bin), $"{projectName}.dll");
                var commonFilePath = Path.Combine(PathProvider.GetPath(PathName.Bin), $"StatePipes.dll");
                var assemblyLoadContext = new AssemblyLoadContext("Common");
                var commonAssembly = assemblyLoadContext.LoadFromAssemblyPath(commonFilePath);
                var commonTypes = commonAssembly.GetTypesNoExceptions();
                var iStateMachineType = commonTypes.FirstOrDefault(t => t.FullName == "StatePipes.Interfaces.IStateMachine");
                var assemblies = assemblyLoadContext.LoadFromAssemblyPath(assemblyFilePath);
                var types = assemblies.GetTypesNoExceptions();
                var stateMachineTypes = types.Where(t => t.IsInterface && iStateMachineType!.IsAssignableFrom(t)).ToArray();
                stateMachineOptions = stateMachineTypes.ToList();
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
            if (stateMachineOptions.Count() == 1) return stateMachineOptions[0];
            if (stateMachineOptions.Count() > 1) return SelectStateMachine(stateMachineOptions);
            return null;
        }
        private Type SelectStateMachine(List<Type> names)
        {
            Size size = new Size(700, 200);
            Form inputBox = new Form();
            inputBox.Size = size;
            inputBox.FormBorderStyle = FormBorderStyle.FixedDialog;
            inputBox.ClientSize = size;
            inputBox.Text = "Select State Machine:";
            var typeMap = new Dictionary<string, Type>();
            ListBox listBox = new ListBox();
            foreach (Type type in names)
            {
                listBox.Items.Add(type.FullName!);
                typeMap.Add(type.FullName!, type);
            }
            listBox.Size = new Size(size.Width - 80 - 80 - 80, size.Height - 30);
            inputBox.Controls.Add(listBox);
            Button okButton = new Button();
            okButton.DialogResult = DialogResult.OK;
            okButton.Name = "okButton";
            okButton.Size = new Size(75, 23);
            okButton.Text = "&OK";
            okButton.Location = new Point(size.Width - 80 - 80, size.Height - 30);
            inputBox.Controls.Add(okButton);
            Button cancelButton = new Button();
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.Name = "cancelButton";
            cancelButton.Size = new Size(75, 23);
            cancelButton.Text = "&Cancel";
            cancelButton.Location = new Point(size.Width - 80, size.Height - 30);
            inputBox.Controls.Add(cancelButton);
            inputBox.AcceptButton = okButton;
            inputBox.CancelButton = cancelButton;
            if (inputBox.ShowDialog() == DialogResult.OK)
            {
                string selectedItem = listBox.SelectedItem?.ToString() ?? string.Empty;
                return typeMap[selectedItem];
            }
            else throw new OperationCanceledException();
        }
        public string GetTypeName(string typeFullName)
        {
            var t = Assemblies.GetTypeOf(typeFullName);
            if (t == null) t = Assemblies.GetTypeOf(GetTypeFromFullNameString(typeFullName));
            if (t == null) return GetTypeFromFullNameString(RemoveJunk(typeFullName));
            return RemoveJunk(t.Name);
        }
        private string GetTypeFromFullNameStringJustType(string typeFullName)
        {
            NamespaceList.AddNameSpaceFromTypeFullName(typeFullName, Assemblies);
            return typeFullName.Split('.').Last();
        }
        private string GetTypeFromFullNameString(string typeFullName)
        {
            var junkRemoved = RemoveJunk(typeFullName);
            return junkRemoved.StartsWith("StatePipes.Comms") && !junkRemoved.StartsWith("StatePipes.Common.") ? junkRemoved.Replace(".", "_") : 
                GetTypeFromFullNameStringJustType(typeFullName);
        }
        private string RemoveAferAndIncluding(string str, string delimeter)
        {
            var indexOf = str.IndexOf(delimeter);
            if (indexOf < 0) return str;
            return str.Substring(0, indexOf);
        }
        public string RemoveJunk(string typeFullName)
        {
            string ret = RemoveAferAndIncluding(typeFullName, "<");
            ret = RemoveAferAndIncluding(ret, "(");
            ret = RemoveAferAndIncluding(ret, "'");
            ret = RemoveAferAndIncluding(ret, "`");
            ret = RemoveAferAndIncluding(ret, "~");
            ret = RemoveAferAndIncluding(ret, "[");
            ret = RemoveAferAndIncluding(ret, ",");
            ret = ret.Replace("+", ".");
            return ret;
        }
        public void CreateHelperContructor(string eventName, string valueObjectName)
        {
            if (!ValueObjectContructorParametersDictionary.ContainsKey(valueObjectName)) return;
            string constructorString = $"public {eventName}(";
            string parameterString = string.Empty;
            foreach (var p in ValueObjectContructorParametersDictionary[valueObjectName])
            {
                if (!string.IsNullOrEmpty(parameterString)) parameterString += ", ";
                parameterString += $"{p.Value} {p.Key}";
            }
            constructorString += parameterString;
            if (!string.IsNullOrEmpty(parameterString)) constructorString += ", ";
            constructorString += $"string proxyName = \"\") : this(proxyName, new {valueObjectName}(";
            var thisParametersString = string.Empty;
            foreach (var p in ValueObjectContructorParametersDictionary[valueObjectName])
            {
                if (!string.IsNullOrEmpty(thisParametersString)) thisParametersString += ", ";
                thisParametersString += $"{p.Key}";
            }
            constructorString += $"{thisParametersString})){{}}";
            CodeGenerationString.AppendTabbedLine(constructorString);
        }
    }
}
