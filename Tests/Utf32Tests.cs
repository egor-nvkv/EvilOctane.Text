using NUnit.Framework;
using System;
using System.Text;

namespace EvilOctane.Text
{
    public sealed unsafe class Utf32Tests
    {
        public static void ForeachValidCodepoint(Action<uint> callback)
        {
            for (uint c = 0x0; c <= 0xd7ff; ++c)
            {
                callback(c);
            }

            for (uint c = 0xe000; c <= 0x10ffff; ++c)
            {
                callback(c);
            }
        }

        public static void ForeachSurrogate(Action<uint> callback)
        {
            uint c = 0xd800;

            for (; c <= 0xdfff; ++c)
            {
                callback(c);
            }
        }

        public static Span<byte> Utf32ToUtf8(uint c, Span<byte> utf8Bytes)
        {
            Span<byte> utf32Bytes = new(&c, sizeof(uint));
            Span<char> utf16Chars = stackalloc char[2];

            // UTF32 -> UTF16
            int utf16CharCount = Encoding.UTF32.GetChars(utf32Bytes, utf16Chars);
            utf16Chars = utf16Chars[..utf16CharCount];

            // UTF16 -> UTF8
            int utf8ByteCount = Encoding.UTF8.GetBytes(utf16Chars, utf8Bytes);
            return utf8Bytes[..utf8ByteCount];
        }

        [Test]
        public void TestIsValidCodepoint()
        {
            ForeachValidCodepoint((uint c) => Assert.IsTrue(Utf32.IsValidCodepoint(c)));
            Assert.IsFalse(Utf32.IsValidCodepoint(0x7fffffff));
        }

        [Test]
        public void TestIsValidCodepoint_Surrogate()
        {
            ForeachSurrogate((uint c) => Assert.IsFalse(Utf32.IsValidCodepoint(c)));
        }

        [Test]
        public void TestGetUtf8ByteCountUnchecked()
        {
            // 1 byte
            Assert.AreEqual(1, Utf32.GetUtf8ByteCountUnchecked('\0'));
            Assert.AreEqual(1, Utf32.GetUtf8ByteCountUnchecked('a'));
            Assert.AreEqual(1, Utf32.GetUtf8ByteCountUnchecked('1'));
            Assert.AreEqual(1, Utf32.GetUtf8ByteCountUnchecked(0x7f));

            // 2 bytes
            Assert.AreEqual(2, Utf32.GetUtf8ByteCountUnchecked(0x80));
            Assert.AreEqual(2, Utf32.GetUtf8ByteCountUnchecked(0x7ff));

            // 3 bytes
            Assert.AreEqual(3, Utf32.GetUtf8ByteCountUnchecked(0x800));
            Assert.AreEqual(3, Utf32.GetUtf8ByteCountUnchecked(0xfff));

            // 4 bytes
            Assert.AreEqual(4, Utf32.GetUtf8ByteCountUnchecked(0x10000));
            Assert.AreEqual(4, Utf32.GetUtf8ByteCountUnchecked(0x10ffff));
        }

        [Test]
        public void TestToUtf8Unchecked()
        {
            ForeachValidCodepoint((uint c) =>
            {
                Span<byte> utf8BytesStorage = stackalloc byte[4];
                Span<byte> utf8Bytes = Utf32ToUtf8(c, utf8BytesStorage);

                // Work
                byte* utf8BytesActual = stackalloc byte[4];
                int utf8Index = 0;
                Utf32.ToUtf8Unchecked_Inline(c, utf8BytesActual, ref utf8Index);

                Span<byte> utf8BytesActualSpan = new(utf8BytesActual, utf8Index);
                CollectionAssert.AreEqual(utf8Bytes.ToArray(), utf8BytesActualSpan.ToArray());
            });
        }
    }
}
