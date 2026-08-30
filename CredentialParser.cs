using System;

namespace ClipTyper
{
    public record CredentialSplitResult(
        string Part1,
        string Part2,
        ushort TransitionVk,
        bool TransitionNeedsShift = false
    );

    /// <summary>
    /// Delimiter detector and parser for two-stage credential typing.
    /// </summary>
    public static class CredentialParser
    {
        public const ushort VK_TAB = 0x09;
        public const ushort VK_RETURN = 0x0D;

        /// <summary>
        /// Attempts to split the clipboard text into two stages (e.g. username and password)
        /// based on the configured CredentialMode and detected delimiters.
        /// </summary>
        public static bool TrySplit(
            string input,
            CredentialMode mode,
            string customDelimiter,
            out CredentialSplitResult? result,
            ushort customTransitionVk = VK_TAB)
        {
            result = null;
            if (string.IsNullOrEmpty(input) || mode == CredentialMode.Off)
            {
                return false;
            }

            string text = input;

            if (mode == CredentialMode.TabOnly)
            {
                int tabIdx = text.IndexOf('\t');
                if (tabIdx >= 0)
                {
                    string p1 = text.Substring(0, tabIdx);
                    string p2 = text.Substring(tabIdx + 1);
                    result = new CredentialSplitResult(p1, p2, VK_TAB);
                    return true;
                }
                return false;
            }

            if (mode == CredentialMode.EnterOnly)
            {
                int nlIdx = IndexOfFirstNewline(text, out int nlLen);
                if (nlIdx >= 0)
                {
                    string p1 = text.Substring(0, nlIdx);
                    string p2 = text.Substring(nlIdx + nlLen);
                    result = new CredentialSplitResult(p1, p2, VK_RETURN);
                    return true;
                }
                return false;
            }

            if (mode == CredentialMode.Custom)
            {
                if (!string.IsNullOrWhiteSpace(customDelimiter))
                {
                    int customIdx = text.IndexOf(customDelimiter, StringComparison.Ordinal);
                    if (customIdx >= 0)
                    {
                        string p1 = text.Substring(0, customIdx);
                        string p2 = text.Substring(customIdx + customDelimiter.Length);
                        result = new CredentialSplitResult(p1, p2, customTransitionVk);
                        return true;
                    }
                }
                return false;
            }

            if (mode == CredentialMode.AutoDetect)
            {
                // Priority 1: Tab delimiter
                int tabIdx = text.IndexOf('\t');
                if (tabIdx >= 0)
                {
                    string p1 = text.Substring(0, tabIdx);
                    string p2 = text.Substring(tabIdx + 1);
                    result = new CredentialSplitResult(p1, p2, VK_TAB);
                    return true;
                }

                // Priority 2: Newline delimiter
                int nlIdx = IndexOfFirstNewline(text, out int nlLen);
                if (nlIdx >= 0)
                {
                    string p1 = text.Substring(0, nlIdx);
                    string p2 = text.Substring(nlIdx + nlLen);
                    result = new CredentialSplitResult(p1, p2, VK_RETURN);
                    return true;
                }
            }

            return false;
        }

        private static int IndexOfFirstNewline(string text, out int newlineLength)
        {
            newlineLength = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        newlineLength = 2;
                        return i;
                    }
                    newlineLength = 1;
                    return i;
                }
                if (text[i] == '\n')
                {
                    newlineLength = 1;
                    return i;
                }
            }
            return -1;
        }
    }
}
