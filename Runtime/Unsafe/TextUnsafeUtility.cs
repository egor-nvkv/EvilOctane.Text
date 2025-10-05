using System;
using System.Runtime.CompilerServices;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Assertions;
using static System.Runtime.CompilerServices.Unsafe;
using static Unity.Burst.Intrinsics.X86.Avx;
using static Unity.Burst.Intrinsics.X86.Avx2;
using static Unity.Burst.Intrinsics.X86.Sse2;
using static Unity.Burst.Intrinsics.X86.Sse4_1;
using static Unity.Collections.CollectionHelper2;

namespace EvilOctane.Text.LowLevel.Unsafe
{
    public static unsafe class TextUnsafeUtility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsStringLiteral(byte* stringLiteral, byte* ptr, int length)
        {
            CheckContainerLength(length);

            Assert.IsTrue(stringLiteral != null);
            Assert.IsTrue(ptr != null);

            if (Constant.IsConstantExpression(true))
            {
                // Burst         

                if (!Constant.IsConstantExpression(length))
                {
                    throw new NotSupportedException($"{nameof(length)} must be a compile time constant.");
                }

                int offset = 0;

                // Multiple of 64
                if (IsAvx2Supported)
                {
                    const int vectorLength64 = 64;
                    int vectorCount64 = (length - offset) / vectorLength64;

                    if (vectorCount64 != 0)
                    {
                        for (int vectorIndex = 0; vectorIndex != vectorCount64; ++vectorIndex)
                        {
                            int vectorOffset = offset + (vectorIndex * vectorLength64);

                            v256 lhs0 = mm256_load_si256(stringLiteral + vectorOffset);
                            v256 lhs1 = mm256_load_si256(stringLiteral + vectorOffset + sizeof(v256));

                            v256 rhs0 = mm256_load_si256(ptr + vectorOffset);
                            v256 rhs1 = mm256_load_si256(ptr + vectorOffset + sizeof(v256));

                            v256 eq0 = mm256_cmpeq_epi8(lhs0, rhs0);
                            v256 eq1 = mm256_cmpeq_epi8(lhs1, rhs1);

                            // If we're looking for name this long, we might as well be optimistic and bunch these together
                            v256 eq = mm256_and_si256(eq0, eq1);

                            if (mm256_testc_si256(eq, new v256((byte)0xff)) == 0)
                            {
                                return false;
                            }
                        }

                        offset += vectorCount64 * vectorLength64;
                    }
                }

                // Multiple of 32
                if (IsAvx2Supported)
                {
                    const int vectorLength32 = 32;
                    int vectorCount32 = (length - offset) / vectorLength32;

                    if (vectorCount32 != 0)
                    {
                        for (int vectorIndex = 0; vectorIndex != vectorCount32; ++vectorIndex)
                        {
                            int vectorOffset = offset + (vectorIndex * vectorLength32);

                            v256 lhs = mm256_load_si256(stringLiteral + vectorOffset);
                            v256 rhs = mm256_load_si256(ptr + vectorOffset);
                            v256 eq = mm256_cmpeq_epi8(lhs, rhs);

                            if (mm256_testc_si256(eq, new v256((byte)0xff)) == 0)
                            {
                                return false;
                            }
                        }

                        offset += vectorCount32 * vectorLength32;
                    }
                }

                // Multiple of 16
                if (IsSse41Supported)
                {
                    const int vectorLength16 = 16;
                    int vectorCount16 = (length - offset) / vectorLength16;

                    for (int vectorIndex = 0; vectorIndex != vectorCount16; ++vectorIndex)
                    {
                        int vectorOffset = offset + (vectorIndex * vectorLength16);

                        if (IsSse41Supported)
                        {
                            v128 lhs = loadu_si128(stringLiteral + vectorOffset);
                            v128 rhs = loadu_si128(ptr + vectorOffset);
                            v128 eq = cmpeq_epi8(lhs, rhs);

                            if (testc_si128(eq, new v128((byte)0xff)) == 0)
                            {
                                return false;
                            }
                        }
                        else
                        {
                            throw new NotSupportedException();
                        }
                    }

                    offset += vectorCount16 * vectorLength16;
                }

                // Multiple of 8
                {
                    const int vectorLength8 = 8;
                    int vectorCount8 = (length - offset) / vectorLength8;

                    for (int vectorIndex = 0; vectorIndex != vectorCount8; ++vectorIndex)
                    {
                        int vectorOffset = offset + (vectorIndex * vectorLength8);

                        ulong lhs = ReadUnaligned<ulong>(stringLiteral + vectorOffset);
                        ulong rhs = ReadUnaligned<ulong>(ptr + vectorOffset);

                        if (lhs != rhs)
                        {
                            return false;
                        }
                    }

                    offset += vectorCount8 * vectorLength8;
                }

                int tailCount = length - offset;

                if (!Constant.IsConstantExpression(tailCount))
                {
                    throw new InvalidOperationException($"{nameof(tailCount)} must be compile time computable.");
                }

                if (tailCount >= 8)
                {
                    throw new InvalidOperationException("Tail of 0-7 bytes expected.");
                }

                ulong expected = 0x0;
                UnsafeUtility.MemCpy(&expected, stringLiteral + offset, tailCount);

                ulong actual = 0x0;

                switch (tailCount)
                {
                    case 0:
                        return true;

                    case 1:
                        UnsafeUtility.MemCpy(&actual, ptr + offset, tailCount);
                        break;

                    case 2:
                        UnsafeUtility.MemCpy(&actual, ptr + offset, tailCount);
                        break;

                    case 3:
                        UnsafeUtility.MemCpy(&actual, ptr + offset, tailCount);
                        break;

                    case 4:
                        UnsafeUtility.MemCpy(&actual, ptr + offset, tailCount);
                        break;

                    case 5:
                        UnsafeUtility.MemCpy(&actual, ptr + offset, tailCount);
                        break;

                    case 6:
                        UnsafeUtility.MemCpy(&actual, ptr + offset, tailCount);
                        break;

                    case 7:
                        UnsafeUtility.MemCpy(&actual, ptr + offset, tailCount);
                        break;

                    default:
                        return false;
                }

                return expected == actual;
            }
            else
            {
                // No Burst

                ByteSpan expected = new(stringLiteral, length);
                ByteSpan actual = new(ptr, length);
                return actual.Equals(expected);
            }
        }
    }
}
