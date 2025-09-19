using NUnit.Framework;
using System;
using System.Text;

namespace EvilOctane.Text
{
    public sealed unsafe class Utf8Tests
    {
        public static uint Utf8ToUtf32(ReadOnlySpan<byte> utf8Bytes, out int byteCount)
        {
            Span<char> utf16Chars = stackalloc char[2];

            // UTF8 -> UTF16
            int utf16CharCount = Encoding.UTF8.GetChars(utf8Bytes, utf16Chars);
            utf16Chars = utf16Chars[..utf16CharCount];

            // UTF16 -> UTF32
            uint c = 0x0;
            Span<byte> utf32Bytes = new(&c, sizeof(uint));
            byteCount = Encoding.UTF32.GetBytes(utf16Chars, utf32Bytes);

            return c;
        }

        [Test]
        public void TestToUtf32NonEmpty()
        {
            Utf32Tests.ForeachValidCodepoint((uint c) =>
            {
                Span<byte> utf8BytesStorage = stackalloc byte[4];
                Span<byte> utf8Bytes = Utf32Tests.Utf32ToUtf8(c, utf8BytesStorage);

                int index = 0;
                ConversionResult conversionResult;
                uint cActual;

                fixed (byte* buffer = utf8Bytes)
                {
                    conversionResult = Utf8.ToUtf32NonEmpty(buffer, ref index, utf8Bytes.Length, out cActual, out _, out _);
                }

                Assert.AreEqual(ConversionResult.Success, conversionResult);
                Assert.AreEqual(utf8Bytes.Length, index);
                Assert.AreEqual(c, cActual);
            });
        }

        [Test]
        public void TestToUtf32NonEmpty_BadLeader()
        {
            // 1 byte
            {
                byte* utf8Bytes = stackalloc byte[1] { 0x81 };
                int index = 0;
                Assert.AreEqual(ConversionResult.BadEncoding, Utf8.ToUtf32NonEmpty(utf8Bytes, ref index, 1, out _, out _, out _));
                Assert.AreEqual(1, index);
            }
            // 2 bytes
            {
                byte* utf8Bytes = stackalloc byte[2] { 0xc0, 0x80 };
                int index = 0;
                Assert.AreEqual(ConversionResult.BadEncoding, Utf8.ToUtf32NonEmpty(utf8Bytes, ref index, 2, out _, out _, out _));
                Assert.AreEqual(1, index);
            }
        }

        [Test]
        public void TestToUtf32NonEmpty_BadTrailer()
        {
            // 2 bytes
            {
                byte* utf8Bytes = stackalloc byte[2] { 0xc3, 0x10 };
                int index = 0;
                Assert.AreEqual(ConversionResult.BadEncoding, Utf8.ToUtf32NonEmpty(utf8Bytes, ref index, 2, out _, out _, out _));
                Assert.AreEqual(1, index);
            }
        }

        [Test]
        public void TestToUtf32NonEmpty_Overflow()
        {
            // 2 bytes
            {
                byte* utf8Bytes = stackalloc byte[1] { 0xc0 };
                int index = 0;
                Assert.AreEqual(ConversionResult.Overflow, Utf8.ToUtf32NonEmpty(utf8Bytes, ref index, 1, out _, out _, out _));
                Assert.AreEqual(1, index);
            }

            // 3 bytes
            {
                byte* utf8Bytes = stackalloc byte[1] { 0xe0 };
                int index = 0;
                Assert.AreEqual(ConversionResult.Overflow, Utf8.ToUtf32NonEmpty(utf8Bytes, ref index, 1, out _, out _, out _));
                Assert.AreEqual(1, index);
            }
            {
                byte* utf8Bytes = stackalloc byte[2] { 0xe0, 0xa0 };
                int index = 0;
                Assert.AreEqual(ConversionResult.Overflow, Utf8.ToUtf32NonEmpty(utf8Bytes, ref index, 2, out _, out _, out _));
                Assert.AreEqual(1, index);
            }

            // 4 bytes
            {
                byte* utf8Bytes = stackalloc byte[1] { 0xf0 };
                int index = 0;
                Assert.AreEqual(ConversionResult.Overflow, Utf8.ToUtf32NonEmpty(utf8Bytes, ref index, 1, out _, out _, out _));
                Assert.AreEqual(1, index);
            }
            {
                byte* utf8Bytes = stackalloc byte[2] { 0xf0, 0x90 };
                int index = 0;
                Assert.AreEqual(ConversionResult.Overflow, Utf8.ToUtf32NonEmpty(utf8Bytes, ref index, 2, out _, out _, out _));
                Assert.AreEqual(1, index);
            }
            {
                byte* utf8Bytes = stackalloc byte[3] { 0xf0, 0x90, 0x80 };
                int index = 0;
                Assert.AreEqual(ConversionResult.Overflow, Utf8.ToUtf32NonEmpty(utf8Bytes, ref index, 3, out _, out _, out _));
                Assert.AreEqual(1, index);
            }
        }

        [Test]
        public void TestToUtf32NonEmpty_Overlong()
        {
            // 2 bytes
            {
                byte* utf8Bytes = stackalloc byte[2] { 0xc0, (byte)'"' };
                int index = 0;
                Assert.AreEqual(ConversionResult.BadEncoding, Utf8.ToUtf32NonEmpty(utf8Bytes, ref index, 2, out _, out _, out _));
                Assert.AreEqual(1, index);
            }
            {
                byte* utf8Bytes = stackalloc byte[2] { 0xc0, (byte)'\\' };
                int index = 0;
                Assert.AreEqual(ConversionResult.BadEncoding, Utf8.ToUtf32NonEmpty(utf8Bytes, ref index, 2, out _, out _, out _));
                Assert.AreEqual(1, index);
            }
        }
    }
}
