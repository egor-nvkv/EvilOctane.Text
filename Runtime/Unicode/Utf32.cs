using System.Runtime.CompilerServices;
using Unity.Burst.CompilerServices;
using static Unity.Burst.Intrinsics.X86.Avx2;
using static Unity.Burst.Intrinsics.X86.Bmi2;

namespace EvilOctane.Text
{
    /// <summary>
    /// <see href="https://datatracker.ietf.org/doc/html/rfc3629"/>
    /// <see href="https://www.unicode.org/versions/Unicode16.0.0/UnicodeStandard-16.0.pdf"/>
    /// </summary>
    public static unsafe class Utf32
    {
        public const uint MaxCodepointUtf8OneByte = 0x7f;
        public const uint MaxCodepointUtf8TwoBytes = 0x7ff;
        public const uint MaxCodepointUtf8ThreeBytes = 0xffff;

        public const uint MaxCodepointBmp = 0xffff;
        public const uint MaxCodepoint = 0x10ffff;

        public const uint Replacement = 0xfffd;

        private static readonly uint[] bitMaskLut = { 0x1f3f, 0x0f3f3f, 0x073f3f3f };
        private static readonly uint[] utf8TrailLeadMaskLut = { 0xc080, 0xe08080, 0xf0808080 };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidCodepoint(uint c)
        {
            return (c <= 0xd7ff) | ((c >= 0xe000) & (c <= MaxCodepoint));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint AssumeIsValidCodepoint(uint c)
        {
            Hint.Assume(IsValidCodepoint(c));
            return c;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsControl(uint c)
        {
            return c <= 0x1f | ((c >= 0x7f) & (c <= 0x9f));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsUtf8TwoByteControl(uint c)
        {
            return (c >= 0x80) & (c <= 0x9f);
        }

        [return: AssumeRange(1, 4)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetUtf8ByteCountUnchecked(uint c)
        {
            if (c <= MaxCodepointUtf8OneByte)
            {
                //
                return 1;
            }

            return 2 +
                (c > MaxCodepointUtf8TwoBytes ? 1 : 0) +
                (c > MaxCodepointUtf8ThreeBytes ? 1 : 0);
        }

        [return: AssumeRange(1, 4)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetUtf8ByteCountUncheckedBranchless(uint c)
        {
            return 1 +
                (c > MaxCodepointUtf8OneByte ? 1 : 0) +
                (c > MaxCodepointUtf8TwoBytes ? 1 : 0) +
                (c > MaxCodepointUtf8ThreeBytes ? 1 : 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToUtf8Unchecked_Inline(uint c, byte* buffer, ref int index)
        {
            int byteCount = GetUtf8ByteCountUnchecked(c);

            if (Hint.Likely(byteCount == 1))
            {
                buffer[index++] = (byte)c;
                return;
            }

            if (IsAvx2Supported)
            {
                int shift = 8 * (4 - byteCount);

                uint bitMaskLead = (uint)((0x7f >> byteCount) << 24);
                uint bitMask = (bitMaskLead | 0x3f3f3f) >> shift;

                uint utf8LeadMask = ~(0xffffffff >> byteCount);
                uint utf8TrailLeadMask = (utf8LeadMask | 0x808080) >> shift;

                uint utf8Nibbles = pdep_u32(c, bitMask) | utf8TrailLeadMask;

                switch (byteCount)
                {
                    case 2:
                        buffer[index] = (byte)(utf8Nibbles >> 8);
                        buffer[index + 1] = (byte)(utf8Nibbles & 0xff);

                        index += 2;
                        break;

                    case 3:
                        buffer[index] = (byte)(utf8Nibbles >> 16);
                        buffer[index + 1] = (byte)((utf8Nibbles >> 8) & 0xff);
                        buffer[index + 2] = (byte)(utf8Nibbles & 0xff);

                        index += 3;
                        break;

                    case 4:
                    default:
                        buffer[index] = (byte)(utf8Nibbles >> 24);
                        buffer[index + 1] = (byte)((utf8Nibbles >> 16) & 0xff);
                        buffer[index + 2] = (byte)((utf8Nibbles >> 8) & 0xff);
                        buffer[index + 3] = (byte)(utf8Nibbles & 0xff);

                        index += 4;
                        break;
                }
            }
            else
            {
                switch (byteCount)
                {
                    case 2:
                        Hint.Assume(c <= MaxCodepointUtf8TwoBytes);

                        buffer[index] = (byte)(0xc0 | (c >> 6));
                        buffer[index + 1] = (byte)(0x80 | ((c >> 0) & 0x3f));

                        index += 2;
                        break;

                    case 3:
                        Hint.Assume(c <= MaxCodepointUtf8ThreeBytes);

                        buffer[index] = (byte)(0xe0 | (c >> 12));
                        buffer[index + 1] = (byte)(0x80 | ((c >> 6) & 0x3f));
                        buffer[index + 2] = (byte)(0x80 | ((c >> 0) & 0x3f));

                        index += 3;
                        break;

                    case 4:
                    default:
                        buffer[index] = (byte)(0xf0 | (c >> 18));
                        buffer[index + 1] = (byte)(0x80 | ((c >> 12) & 0x3f));
                        buffer[index + 2] = (byte)(0x80 | ((c >> 6) & 0x3f));
                        buffer[index + 3] = (byte)(0x80 | ((c >> 0) & 0x3f));

                        index += 4;
                        break;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToUtf8Unchecked_InlineLut(uint c, byte* buffer, ref int index)
        {
            if (IsAvx2Supported)
            {
                int byteCount = GetUtf8ByteCountUnchecked(c);

                if (Hint.Likely(byteCount == 1))
                {
                    buffer[index++] = (byte)c;
                    return;
                }

                // https://nrk.neocities.org/articles/utf8-pext

                int lutIndex = byteCount - 2;

                uint bitMask = bitMaskLut[lutIndex];
                uint utf8TrailLeadMask = utf8TrailLeadMaskLut[lutIndex];

                uint utf8Nibbles = pdep_u32(c, bitMask) | utf8TrailLeadMask;

                switch (byteCount)
                {
                    case 2:
                        buffer[index] = (byte)(utf8Nibbles >> 8);
                        buffer[index + 1] = (byte)(utf8Nibbles & 0xff);

                        index += 2;
                        break;

                    case 3:
                        buffer[index] = (byte)(utf8Nibbles >> 16);
                        buffer[index + 1] = (byte)((utf8Nibbles >> 8) & 0xff);
                        buffer[index + 2] = (byte)(utf8Nibbles & 0xff);

                        index += 3;
                        break;

                    case 4:
                    default:
                        buffer[index] = (byte)(utf8Nibbles >> 24);
                        buffer[index + 1] = (byte)((utf8Nibbles >> 16) & 0xff);
                        buffer[index + 2] = (byte)((utf8Nibbles >> 8) & 0xff);
                        buffer[index + 3] = (byte)(utf8Nibbles & 0xff);

                        index += 4;
                        break;
                }
            }
            else
            {
                ToUtf8Unchecked_Inline(c, buffer, ref index);
            }
        }
    }
}
