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
    /// Only text that consists of exactly two non-empty parts (username + password) is treated as
    /// credentials; anything else (scripts, multi-line text, tables) is typed normally.
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

            // Trailing line breaks (common when copying from password managers or tables) are not a delimiter
            string text = input.TrimEnd('\r', '\n');

            return mode switch
            {
                CredentialMode.TabOnly => TrySplitOnTab(text, out result),
                CredentialMode.EnterOnly => TrySplitOnNewline(text, out result),
                CredentialMode.Custom => TrySplitOnCustom(text, customDelimiter, customTransitionVk, out result),
                CredentialMode.AutoDetect => TrySplitOnTab(text, out result) || TrySplitOnNewline(text, out result),
                _ => false
            };
        }

        /// <summary>user&lt;Tab&gt;pass: exactly one Tab and no line break.</summary>
        private static bool TrySplitOnTab(string text, out CredentialSplitResult? result)
        {
            result = null;
            int tabIdx = text.IndexOf('\t');
            if (tabIdx < 0 || text.IndexOf('\t', tabIdx + 1) >= 0 || CountLineBreaks(text) != 0)
            {
                return false;
            }

            return TryCreate(text.Substring(0, tabIdx), text.Substring(tabIdx + 1), VK_TAB, out result);
        }

        /// <summary>user&lt;newline&gt;pass: exactly one line break and no Tab.</summary>
        private static bool TrySplitOnNewline(string text, out CredentialSplitResult? result)
        {
            result = null;
            if (CountLineBreaks(text) != 1 || text.Contains('\t'))
            {
                return false;
            }

            int nlIdx = IndexOfFirstNewline(text, out int nlLen);
            return TryCreate(text.Substring(0, nlIdx), text.Substring(nlIdx + nlLen), VK_RETURN, out result);
        }

        /// <summary>user&lt;delimiter&gt;pass: the delimiter exactly once and no line break.</summary>
        private static bool TrySplitOnCustom(string text, string customDelimiter, ushort transitionVk, out CredentialSplitResult? result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(customDelimiter) || CountLineBreaks(text) != 0)
            {
                return false;
            }

            int customIdx = text.IndexOf(customDelimiter, StringComparison.Ordinal);
            if (customIdx < 0 || text.IndexOf(customDelimiter, customIdx + customDelimiter.Length, StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            return TryCreate(text.Substring(0, customIdx), text.Substring(customIdx + customDelimiter.Length), transitionVk, out result);
        }

        private static bool TryCreate(string part1, string part2, ushort transitionVk, out CredentialSplitResult? result)
        {
            result = null;
            if (part1.Length == 0 || part2.Length == 0)
            {
                return false;
            }

            result = new CredentialSplitResult(part1, part2, transitionVk);
            return true;
        }

        /// <summary>Counts line breaks; CRLF counts as one.</summary>
        private static int CountLineBreaks(string text)
        {
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    count++;
                    if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                }
                else if (text[i] == '\n')
                {
                    count++;
                }
            }
            return count;
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
