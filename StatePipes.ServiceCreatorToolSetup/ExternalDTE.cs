using System.Runtime.InteropServices;
using EnvDTE80;
using Microsoft.VisualStudio.OLE.Interop;

namespace StatePipes.ServiceCreatorToolSetup
{
    public class ExternalDTE
    {
        public static DTE2? GetDTE2(int processId)
        {
            IMoniker[] moniker = new IMoniker[1];
            GetRunningObjectTable(0, out IRunningObjectTable rot);
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
        private static extern int GetRunningObjectTable(uint reserved, out IRunningObjectTable pprot);
        [DllImport("ole32.dll")]
        private static extern int CreateBindCtx(uint reserved, out IBindCtx ppbc);
    }
}
