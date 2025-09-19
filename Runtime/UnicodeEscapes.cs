using System.Runtime.CompilerServices;
using Unity.Burst.CompilerServices;
using Unity.Mathematics;
using UnityEngine.Assertions;
using static System.Runtime.CompilerServices.Unsafe;

namespace EvilOctane.Text
{
    public static unsafe class UnicodeEscapes
    {
        public const int UnicodeEscapePrefixLength = 2; // \u

        public const int UnicodeEscapeBodyLength = 4; // ab01
        public const int UnicodeEscapeLength = UnicodeEscapePrefixLength + UnicodeEscapeBodyLength;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckUnicodeEscapePrefix(byte* buffer, int index)
        {
            return (buffer[index] == (byte)'\\') & (buffer[index + 1] == (byte)'u');
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckUnicodeEscapeLengthAndPrefix(byte* buffer, int index, int length)
        {
            if (index + UnicodeEscapeLength > length)
            {
                //
                return false;
            }

            return CheckUnicodeEscapePrefix(buffer, index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool4 GetInvalidUnicodeEscapeCharacterMask(uint4 c)
        {
            uint4 lower = c | 0x20;

            bool4 notDigit = (c - '0') > ('9' - '0');
            bool4 notHex = (lower - 'a') > ('f' - 'a');

            return notDigit & notHex;
        }

        [return: AssumeRange(-1, 3)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOfInvalidUnicodeEscapeCharacter(uint4 c)
        {
            bool4 invalid = GetInvalidUnicodeEscapeCharacterMask(c);
            int mask = math.bitmask(invalid);
            return mask == 0x0 ? -1 : math.tzcnt(mask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint4 ParseAsciiHexDigitUnchecked(uint4 c)
        {
            uint4 alpha = c & 0x40;
            return (c & 0xf) + (alpha >> 3) + (alpha >> 6);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ConversionResult UnicodeEscapeToUtf32(byte* buffer, ref int index, int length, out uint codepoint, out bool2 isHiLoSurrogate)
        {
            ConversionResult failedResult;

            if (Hint.Unlikely(index + UnicodeEscapeLength > length))
            {
                index = length;
                failedResult = ConversionResult.Overflow;
                goto Failed;
            }

            byte c0 = buffer[index + UnicodeEscapePrefixLength];
            byte c1 = buffer[index + UnicodeEscapePrefixLength + 1];
            byte c2 = buffer[index + UnicodeEscapePrefixLength + 2];
            byte c3 = buffer[index + UnicodeEscapePrefixLength + 3];

            codepoint = UnicodeEscapeAsciiHexDigitsToCodepoint(c0, c1, c2, c3, out int indexOfInvalid, out isHiLoSurrogate);

            if (Hint.Unlikely(indexOfInvalid >= 0))
            {
                index += UnicodeEscapePrefixLength + indexOfInvalid;
                failedResult = ConversionResult.BadEncoding;
                goto Failed;
            }

            index += UnicodeEscapeLength;
            return ConversionResult.Success;

        Failed:
            codepoint = Utf32.Replacement;
            SkipInit(out isHiLoSurrogate);
            return failedResult;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint UnicodeEscapeToUtf32Unchecked(byte* buffer, ref int index)
        {
            byte c0 = buffer[index + UnicodeEscapePrefixLength];
            byte c1 = buffer[index + UnicodeEscapePrefixLength + 1];
            byte c2 = buffer[index + UnicodeEscapePrefixLength + 2];
            byte c3 = buffer[index + UnicodeEscapePrefixLength + 3];

            index += UnicodeEscapeLength;
            return UnicodeEscapeAsciiHexDigitsToCodepoint(c0, c1, c2, c3, out _, out _);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Utf8ToUnicodeEscape(uint codepoint, byte* dest, ref int destIndex)
        {
            Assert.IsTrue(codepoint <= 0xffff);

            byte nb0 = (byte)((codepoint >> 12) & 0xf);
            byte nb1 = (byte)((codepoint >> 8) & 0xf);
            byte nb2 = (byte)((codepoint >> 4) & 0xf);
            byte nb3 = (byte)(codepoint & 0xf);

            uint4 nb = new(nb0, nb1, nb2, nb3);
            uint4 digit = nb + '0';

            uint4 hexMask = (9 - nb) >> 7;
            uint4 hexOffset = 'A' - '0' - 10;
            uint4 hex = hexMask & hexOffset;

            uint4 result = digit + hex;

            dest[destIndex] = (byte)'\\';
            dest[destIndex + 1] = (byte)'u';

            dest[destIndex + 2] = (byte)result.x;
            dest[destIndex + 3] = (byte)result.y;
            dest[destIndex + 4] = (byte)result.z;
            dest[destIndex + 5] = (byte)result.w;

            destIndex += UnicodeEscapeLength;
        }

        [return: AssumeRange(0, Utf32.MaxCodepointBmp)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint UnicodeEscapeAsciiHexDigitsToCodepoint(byte c0, byte c1, byte c2, byte c3, out int indexOfInvalid, out bool2 isHiLoSurrogate)
        {
            uint4 c = new(c0, c1, c2, c3);
            indexOfInvalid = IndexOfInvalidUnicodeEscapeCharacter(c);

            uint4 dec = ParseAsciiHexDigitUnchecked(c);
            isHiLoSurrogate = (dec.xx == 0xd) & ((dec.yy & 0xc) == new uint2(0x8, 0xc));

            uint4 term = new(
                dec.x << (4 * (3 - 0)),
                dec.y << (4 * (3 - 1)),
                dec.z << (4 * (3 - 2)),
                dec.w << (4 * (3 - 3)));

            uint result = math.csum(term);
            return result;
        }
    }
}
