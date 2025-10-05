using System.Runtime.CompilerServices;
using Unity.Burst.CompilerServices;

namespace EvilOctane.Text
{
    /// <summary>
    /// <see href="https://www.unicode.org/versions/Unicode16.0.0/UnicodeStandard-16.0.pdf"/>
    /// </summary>
    public static class Utf16
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsHiSurrogate(uint c)
        {
            return (c >= 0xd800) & (c <= 0xdbff);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLoSurrogate(uint c)
        {
            return (c >= 0xdc00) & (c <= 0xdfff);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint SurrogatePairToUtf32Unchecked(uint hi, uint lo)
        {
            uint c = 0x10000 + ((hi - 0xd800) << 10) + (lo - 0xdc00);

            Hint.Assume(Utf32.IsValidCodepoint(c));
            Hint.Assume(c > Utf32.MaxCodepointBmp);
            return c;
        }
    }
}
