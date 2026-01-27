using System.Text;

namespace StatePipes.ServiceCreatorTool
{
    public static class StringBuilderExtensions
    {
        private static TabGenerator _tabGenerator = new();
        public static void AppendTabbedLine(this StringBuilder sb, string text)
        {
            sb.AppendLine($"{_tabGenerator.TabString}{text}");
        }

        public static void Indent(this StringBuilder sb)
        {
            sb.AppendTabbedLine($"{{");
            _tabGenerator.Indent();
        }
        public static void Outdent(this StringBuilder sb)
        {
            _tabGenerator.Outdent();
            sb.AppendTabbedLine($"}}");
        }

        public static void ResetIndention(this StringBuilder sb)
        {
            _tabGenerator.Reset();
        }
    }
}
