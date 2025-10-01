using System.Runtime.InteropServices;
using System.Security;
using System.Text;
namespace StatePipes.Comms.Internal
{
    internal class SecureFileReader
    {
        public static string ToInsecureString(SecureString secureString)
        {
            if (secureString == null)
            {
                throw new ArgumentNullException(nameof(secureString));
            }
            nint bstr = nint.Zero; // Pointer to unmanaged memory
            try
            {
                bstr = Marshal.SecureStringToBSTR(secureString);
                return Marshal.PtrToStringBSTR(bstr);
            }
            finally
            {
                if (bstr != nint.Zero) Marshal.ZeroFreeBSTR(bstr);
            }
        }
        public static SecureString ReadFileToSecureString(string filePath, bool removeWhiteSpace = true, Encoding? encoding = null)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentNullException(nameof(filePath));
            }
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("File not found.", filePath);
            }
            SecureString secureString = new SecureString();
            if (encoding == null)
            {
                encoding = Encoding.UTF8;
            }
            try
            {
                using (StreamReader reader = new StreamReader(filePath, encoding))
                {
                    int charCode;
                    while ((charCode = reader.Read()) != -1)
                    {
                        char c = (char)charCode;
                        if (removeWhiteSpace && !char.IsWhiteSpace(c))
                        {
                            secureString.AppendChar(c);
                        }
                    }
                }
                secureString.MakeReadOnly();
            }
            catch
            {
                secureString.Dispose();
                throw;
            }
            return secureString;
        }
    }
}