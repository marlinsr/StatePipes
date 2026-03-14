using System.Runtime.InteropServices;
using EnvDTE80;
using Microsoft.VisualStudio.OLE.Interop;

namespace StatePipes.Diagrammer
{
    public class ExternalDTE
    {
        public static DTE2? GetDTE2(int processId)
        {
            IMoniker[] moniker = new IMoniker[1];
            _=GetRunningObjectTable(0, out IRunningObjectTable rot);
            rot.EnumRunning(out IEnumMoniker enumMoniker);
            enumMoniker.Reset();
            while (enumMoniker.Next(1, moniker, out _) == 0)
            {
                _ = CreateBindCtx(0, out IBindCtx bindCtx);
                moniker[0].GetDisplayName(bindCtx, null, out string displayName);
                if (displayName.StartsWith($"!VisualStudio.DTE") && displayName.EndsWith($":{processId}"))
                {
                    rot.GetObject(moniker[0], out object runningObject);
                    return (DTE2)runningObject;
                }
            }
            return null;
        }
        [DllImport("ole32.dll")]
#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
        private static extern int GetRunningObjectTable(uint reserved, out IRunningObjectTable pprot);
#pragma warning restore SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
        [DllImport("ole32.dll")]
#pragma warning disable SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
        private static extern int CreateBindCtx(uint reserved, out IBindCtx ppbc);
#pragma warning restore SYSLIB1054 // Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time
    }
}
