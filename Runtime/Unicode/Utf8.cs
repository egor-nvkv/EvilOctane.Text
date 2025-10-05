using System.Runtime.CompilerServices;
using Unity.Burst.CompilerServices;
using Unity.Mathematics;
using UnityEngine.Assertions;
using static Unity.Burst.Intrinsics.X86.Avx2;
using static Unity.Burst.Intrinsics.X86.Bmi2;

namespace EvilOctane.Text
{
    /// <summary>
    /// <see href="https://datatracker.ietf.org/doc/html/rfc3629"/>
    /// </summary>
    public static unsafe class Utf8
    {
        public const int ReplacementLength = 3;

        public const uint FirstCodepointTwoByte = 0x80;
        public const uint FirstCodepointThreeByte = 0x800;
        public const uint FirstCodepointFourByte = 0x10000;

        private static readonly uint[] bitMaskLut = { 0x1f3f0000, 0x0f3f3f00, 0x073f3f3f };
        private static readonly uint[] firstCodepointLut = { 0x80, 0x800, 0x10000 };
        private static readonly uint[] trailMaskLut = { 0x00c00000, 0x00c0c000, 0x00c0c0c0 };
        private static readonly uint[] trailCheckLut = { 0x00800000, 0x00808000, 0x00808080 };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsOneByte(byte lead)
        {
            return (lead & 0x80) == 0x0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsTwoByte(byte lead)
        {
            return (lead & 0xe0) == 0xc0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsControl(byte c)
        {
            return c is <= 0x1f or 0x7f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsTwoByteControl(byte b0, byte b1)
        {
            return (b0 == 0xc2) & (b1 >= 0x80) & (b1 <= 0x9f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetByteCountUnchecked(byte lead, out bool badEncoding)
        {
            if (IsOneByte(lead))
            {
                badEncoding = false;
                return 1;
            }

            int byteCount = math.lzcnt(~(lead << 24));
            badEncoding = Hint.Unlikely(byteCount < 1 | byteCount > 4);
            return byteCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteReplacementCharacter(byte* buffer, ref int index)
        {
            buffer[index] = 0xef;
            buffer[index] = 0xbf;
            buffer[index] = 0xbd;
            index += ReplacementLength;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ConversionResult ToUtf32NonEmpty(byte* buffer, ref int index, int length, out uint c, out int bytesRead, out int codepointUtf8ByteCount)
        {
            Assert.AreNotEqual(0, length);
            Assert.IsTrue(index < length);

            byte lead = buffer[index];

            if (IsOneByte(lead))
            {
                ++index;

                c = Utf32.AssumeIsValidCodepoint(lead);
                bytesRead = 1;
                codepointUtf8ByteCount = 1;
                return ConversionResult.Success;
            }

            return ToUtf32NonEmptyMultibyte_NoInline(buffer, ref index, length, out c, out bytesRead, out codepointUtf8ByteCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ConversionResult ToUtf32NonEmptyMultibyte_Inline(byte* buffer, ref int index, int length, out uint c, out int bytesRead, out int codepointUtf8ByteCount)
        {
            byte lead = buffer[index];

            Assert.IsTrue(!IsOneByte(lead));
            Hint.Assume(!IsOneByte(lead));

            ConversionResult failedResult = ConversionResult.BadEncoding;
            int byteCount = GetByteCountUnchecked(lead, out bool badEncoding);

            if (Hint.Unlikely(badEncoding))
            {
                goto Failed;
            }

            bool overflow = index + byteCount > length;

            if (Hint.Unlikely(overflow))
            {
                failedResult = ConversionResult.Overflow;
                goto Failed;
            }

            if (IsAvx2Supported)
            {
                uint utf8Nibbles = byteCount switch
                {
                    2 => (uint)(
                    (buffer[index] << 24) |
                    (buffer[index + 1] << 16)),

                    3 => (uint)(
                    (buffer[index] << 24) |
                    (buffer[index + 1] << 16) |
                    (buffer[index + 2] << 8)),

                    _ => (uint)(
                    (buffer[index] << 24) |
                    (buffer[index + 1] << 16) |
                    (buffer[index + 2] << 8) |
                    (buffer[index + 3] << 0))
                };

                int shift = 8 * (4 - byteCount);

                uint bitMaskLead = (uint)((0x7f >> byteCount) << 24);
                uint bitMaskTrail = (uint)((0x3f3f3f << shift) & 0x3f3f3f);

                uint bitMask = bitMaskLead | bitMaskTrail;
                c = pext_u32(utf8Nibbles, bitMask);

                // 2 -> 0
                // 3 -> 4
                // 4 -> 9
                int firstCodepointShift = ((byteCount - 2) << 2) + (byteCount >> 2);

                int firstCodepoint = 0x80 << firstCodepointShift;

                uint trailMask = (uint)((0xc0c0c0 << shift) & 0xc0c0c0);
                uint trailCheck = (uint)((0x808080 << shift) & 0x808080);

                bool overlong = c < firstCodepoint;
                bool badCodepoint = !Utf32.IsValidCodepoint(c);
                bool badTrail = (utf8Nibbles & trailMask) != trailCheck;

                if (Hint.Unlikely(overlong | badCodepoint | badTrail))
                {
                    goto Failed;
                }
            }
            else
            {
                switch (byteCount)
                {
                    case 2:
                        c = (uint)(
                            ((buffer[index] & 0x1f) << 6) |
                            ((buffer[index + 1] & 0x3f) << 0));

                        if (Hint.Unlikely(c < FirstCodepointTwoByte || !IsTrailer(buffer[index + 1])))
                        {
                            goto Failed;
                        }

                        break;

                    case 3:
                        c = (uint)(
                            ((buffer[index] & 0x0f) << 12) |
                            ((buffer[index + 1] & 0x3f) << 6) |
                            ((buffer[index + 2] & 0x3f) << 0));

                        if (Hint.Unlikely(c < FirstCodepointThreeByte || !Utf32.IsValidCodepoint(c) || !IsTrailer(buffer[index + 1]) || !IsTrailer(buffer[index + 2])))
                        {
                            goto Failed;
                        }

                        break;

                    case 4:
                    default:
                        c = (uint)(
                            ((buffer[index] & 0x07) << 18) |
                            ((buffer[index + 1] & 0x3f) << 12) |
                            ((buffer[index + 2] & 0x3f) << 6) |
                            ((buffer[index + 3] & 0x3f) << 0));

                        if (Hint.Unlikely(c < FirstCodepointFourByte || !Utf32.IsValidCodepoint(c) || !IsTrailer(buffer[index + 1]) || !IsTrailer(buffer[index + 2]) || !IsTrailer(buffer[index + 3])))
                        {
                            goto Failed;
                        }

                        break;
                }
            }

            index += byteCount;
            bytesRead = byteCount;
            codepointUtf8ByteCount = byteCount;

            _ = Utf32.AssumeIsValidCodepoint(c);
            Hint.Assume(c > Utf32.MaxCodepointUtf8OneByte);

            Hint.Assume(bytesRead is >= 2 and <= 4);
            Hint.Assume(codepointUtf8ByteCount is >= 2 and <= 4);
            return ConversionResult.Success;

        Failed:
            ++index;
            c = Utf32.Replacement;
            bytesRead = 1;
            codepointUtf8ByteCount = ReplacementLength;
            return failedResult;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ConversionResult ToUtf32NonEmptyMultibyte_InlineLut(byte* buffer, ref int index, int length, out uint c, out int bytesRead, out int codepointUtf8ByteCount)
        {
            if (IsAvx2Supported)
            {
                byte lead = buffer[index];

                Assert.IsTrue(!IsOneByte(lead));
                Hint.Assume(!IsOneByte(lead));

                ConversionResult failedResult = ConversionResult.BadEncoding;
                int byteCount = GetByteCountUnchecked(lead, out bool badEncoding);

                if (Hint.Unlikely(badEncoding))
                {
                    goto Failed;
                }

                bool overflow = index + byteCount > length;

                if (Hint.Unlikely(overflow))
                {
                    failedResult = ConversionResult.Overflow;
                    goto Failed;
                }

                uint utf8Nibbles = byteCount switch
                {
                    2 => (uint)(
                    (buffer[index] << 24) |
                    (buffer[index + 1] << 16)),

                    3 => (uint)(
                    (buffer[index] << 24) |
                    (buffer[index + 1] << 16) |
                    (buffer[index + 2] << 8)),

                    _ => (uint)(
                    (buffer[index] << 24) |
                    (buffer[index + 1] << 16) |
                    (buffer[index + 2] << 8) |
                    (buffer[index + 3] << 0))
                };

                // https://nrk.neocities.org/articles/utf8-pext

                int lutIndex = byteCount - 2;

                uint bitMask = bitMaskLut[lutIndex];
                c = pext_u32(utf8Nibbles, bitMask);

                uint firstCodepoint = firstCodepointLut[lutIndex];
                uint trailMask = trailMaskLut[lutIndex];
                uint trailCheck = trailCheckLut[lutIndex];

                bool overlong = c < firstCodepoint;
                bool badCodepoint = !Utf32.IsValidCodepoint(c);
                bool badTrail = (utf8Nibbles & trailMask) != trailCheck;

                if (Hint.Unlikely(overlong | badCodepoint | badTrail))
                {
                    goto Failed;
                }

                index += byteCount;
                bytesRead = byteCount;
                codepointUtf8ByteCount = byteCount;

                _ = Utf32.AssumeIsValidCodepoint(c);
                Hint.Assume(c > Utf32.MaxCodepointUtf8OneByte);

                Hint.Assume(bytesRead is >= 2 and <= 4);
                Hint.Assume(codepointUtf8ByteCount is >= 2 and <= 4);
                return ConversionResult.Success;

            Failed:
                ++index;
                c = Utf32.Replacement;
                bytesRead = 1;
                codepointUtf8ByteCount = ReplacementLength;
                return failedResult;
            }
            else
            {
                return ToUtf32NonEmptyMultibyte_Inline(buffer, ref index, length, out c, out bytesRead, out codepointUtf8ByteCount);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ToUtf32NonEmptyMultibyteUnchecked_InlineLut(byte* buffer, ref int index, int length, out int byteCount)
        {
            if (IsAvx2Supported)
            {
                byte lead = buffer[index];

                Assert.IsTrue(!IsOneByte(lead));
                Hint.Assume(!IsOneByte(lead));

                byteCount = GetByteCountUnchecked(lead, out _);

                uint utf8Nibbles = byteCount switch
                {
                    2 => (uint)(
                    (buffer[index] << 24) |
                    (buffer[index + 1] << 16)),

                    3 => (uint)(
                    (buffer[index] << 24) |
                    (buffer[index + 1] << 16) |
                    (buffer[index + 2] << 8)),

                    _ => (uint)(
                    (buffer[index] << 24) |
                    (buffer[index + 1] << 16) |
                    (buffer[index + 2] << 8) |
                    (buffer[index + 3] << 0))
                };

                // https://nrk.neocities.org/articles/utf8-pext

                int lutIndex = byteCount - 2;

                uint bitMask = bitMaskLut[lutIndex];
                uint c = pext_u32(utf8Nibbles, bitMask);

                index += byteCount;

                _ = Utf32.AssumeIsValidCodepoint(c);
                Hint.Assume(c > Utf32.MaxCodepointUtf8OneByte);

                return c;
            }
            else
            {
                _ = ToUtf32NonEmptyMultibyte_Inline(buffer, ref index, length, out uint c, out _, out byteCount);
                return c;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ConversionResult ToUtf32NonEmptyMultibyte_NoInline(byte* buffer, ref int index, int length, out uint c, out int bytesRead, out int codepointUtf8ByteCount)
        {
            int previousIndex = index;
            ConversionResult result = ToUtf32NonEmptyMultibyte_NoInline_Impl(buffer, ref index, length, out c);

            bytesRead = index - previousIndex;
            codepointUtf8ByteCount = result == ConversionResult.Success ? bytesRead : ReplacementLength;

            // Assumes replacement on error
            Hint.Assume(result == ConversionResult.Success || c == Utf32.Replacement);

            _ = Utf32.AssumeIsValidCodepoint(c);
            Hint.Assume(c > Utf32.MaxCodepointUtf8OneByte);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsTrailer(byte b)
        {
            return (b & 0xc0) == 0x80;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ConversionResult ToUtf32NonEmptyMultibyte_NoInline_Impl(byte* buffer, ref int index, int length, out uint c)
        {
            return ToUtf32NonEmptyMultibyte_Inline(buffer, ref index, length, out c, out _, out _);
        }
    }
}
