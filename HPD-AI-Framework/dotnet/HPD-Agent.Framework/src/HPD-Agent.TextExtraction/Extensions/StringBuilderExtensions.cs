using System.Text;

namespace HPD.Agent.TextExtraction.Extensions
{
    public static class StringBuilderExtensions
    {
        /// <summary>
        /// Append line using Unix line ending "\n"
        /// </summary>
        public static StringBuilder AppendLineNix(this StringBuilder sb)
        {
            sb.Append('\n');
            return sb;
        }

        /// <summary>
        /// Append line using Unix line ending "\n"
        /// </summary>
        public static StringBuilder AppendLineNix(this StringBuilder sb, string value)
        {
            sb.Append(value);
            sb.Append('\n');
            return sb;
        }

        /// <summary>
        /// Append line using Unix line ending "\n"
        /// </summary>
        public static StringBuilder AppendLineNix(this StringBuilder sb, char value)
        {
            sb.Append(value);
            sb.Append('\n');
            return sb;
        }

        /// <summary>
        /// Append line using Unix line ending "\n"
        /// </summary>
        public static StringBuilder AppendLineNix(this StringBuilder sb, StringBuilder value)
        {
            sb.Append(value);
            sb.Append('\n');
            return sb;
        }
    }
}