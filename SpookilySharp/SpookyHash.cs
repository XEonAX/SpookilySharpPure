﻿// SpookyHash.cs
//
// Author:
//     Jon Hanna <jon@hackcraft.net>
//
// © 2014–2017 Jon Hanna
//
// Licensed under the MIT license. See the LICENSE file in the repository root for more details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.Serialization;
using System.Security;
using System.Security.Permissions;
using System.Text.RegularExpressions;

namespace SpookilySharp
{
    /// <summary>Provides an implementation of SpookyHash, either incrementally or (by static methods) in a single
    /// operation.</summary>
    [Serializable]
    public sealed partial class SpookyHash : ISerializable
    {
        internal const ulong SpookyConst = 0xDEADBEEFDEADBEEF;
        private const int NumVars = 12;
        private const int BlockSize = NumVars * 8;
        private const int BufSize = 2 * BlockSize;
        private static readonly bool AllowUnalignedRead = AttemptDetectAllowUnalignedRead();

        [ExcludeFromCodeCoverage]
        private static bool AttemptDetectAllowUnalignedRead()
        {
            switch (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE"))
            {
                case "x86":
                case "AMD64": // Known to tolerate unaligned-reads well.
                    return true;
            }

            try
            {
                return FindAlignSafetyFromUname();
            }
            catch
            {
                return false;
            }
        }

        [SecuritySafeCritical]
        [ExcludeFromCodeCoverage]
        private static bool FindAlignSafetyFromUname()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo("uname", "-p")
            {
                CreateNoWindow = true,
                ErrorDialog = false,
                LoadUserProfile = false,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            try
            {
                using (Process proc = new Process())
                {
                    proc.StartInfo = startInfo;
                    proc.Start();
                    using (StreamReader output = proc.StandardOutput)
                    {
                        string line = output.ReadLine();
                        if (line != null)
                        {
                            string trimmed = line.Trim();
                            if (trimmed.Length != 0)
                            {
                                switch (trimmed)
                                {
                                    case "amd64":
                                    case "i386":
                                    case "x86_64":
                                    case "x64":
                                        return true; // Known to tolerate unaligned-reads well.
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // We don't care why we failed, as there are many possible reasons, and they all amount
                // to our not having an answer. Just eat the exception.
            }

            startInfo.Arguments = "-m";
            try
            {
                using (Process proc = new Process())
                {
                    proc.StartInfo = startInfo;
                    proc.Start();
                    using (StreamReader output = proc.StandardOutput)
                    {
                        string line = output.ReadLine();
                        if (line != null)
                        {
                            string trimmed = line.Trim();
                            if (trimmed.Length != 0)
                            {
                                switch (trimmed)
                                {
                                    case "amd64":
                                    case "i386":
                                    case "i686":
                                    case "i686-64":
                                    case "i86pc":
                                    case "x86_64":
                                    case "x64":
                                        return true; // Known to tolerate unaligned-reads well.
                                    default:
                                        return new Regex(@"i\d86").IsMatch(trimmed);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Again, just eat the exception.
            }

            return false;
        }

        /// <summary>Calculates the 128-bit SpookyHash for a message.</summary>
        /// <param name="message">Pointer to the first element to hash.</param>
        /// <param name="length">The size, in bytes, of the elements to hash.</param>
        /// <param name="hash1">Takes as input a seed value, returns as first output half of the hash.</param>
        /// <param name="hash2">Takes as input a seed value, returns as second output half of the hash.</param>
        /// <remarks>This is not a CLS-compliant method, and is not accessible by some .NET languages.</remarks>
        /// <exception cref="AccessViolationException">This is an unsafe method. If you attempt to read past the buffer
        /// that <paramref name="message"/> points too, you may raise an <see cref="AccessViolationException"/>, or you
        /// may have incorrect results.</exception>
        [CLSCompliant(false), SecurityCritical]
        public static unsafe void Hash128(void* message, int length, ref ulong hash1, ref ulong hash2)
        {
            if ((int)message == 0)
            {
                hash1 = 0;
                hash2 = 0;
                return;
            }

            if (length < BufSize)
            {
                Short(message, length, ref hash1, ref hash2);
                return;
            }

            ulong h0, h1, h2, h3, h4, h5, h6, h7, h8, h9, h10, h11;

            h0 = h3 = h6 = h9 = hash1;
            h1 = h4 = h7 = h10 = hash2;
            h2 = h5 = h8 = h11 = SpookyConst;

            ulong* p64 = (ulong*)message;

            ulong* end = p64 + length / BlockSize * NumVars;
            ulong* buf = stackalloc ulong[NumVars];
            if (AllowUnalignedRead || ((long)message & 7) == 0)
            {
                while (p64 < end)
                {
                    h0 += p64[0];
                    h2 ^= h10;
                    h11 ^= h0;
                    h0 = h0 << 11 | h0 >> -11;
                    h11 += h1;
                    h1 += p64[1];
                    h3 ^= h11;
                    h0 ^= h1;
                    h1 = h1 << 32 | h1 >> 32;
                    h0 += h2;
                    h2 += p64[2];
                    h4 ^= h0;
                    h1 ^= h2;
                    h2 = h2 << 43 | h2 >> -43;
                    h1 += h3;
                    h3 += p64[3];
                    h5 ^= h1;
                    h2 ^= h3;
                    h3 = h3 << 31 | h3 >> -31;
                    h2 += h4;
                    h4 += p64[4];
                    h6 ^= h2;
                    h3 ^= h4;
                    h4 = h4 << 17 | h4 >> -17;
                    h3 += h5;
                    h5 += p64[5];
                    h7 ^= h3;
                    h4 ^= h5;
                    h5 = h5 << 28 | h5 >> -28;
                    h4 += h6;
                    h6 += p64[6];
                    h8 ^= h4;
                    h5 ^= h6;
                    h6 = h6 << 39 | h6 >> -39;
                    h5 += h7;
                    h7 += p64[7];
                    h9 ^= h5;
                    h6 ^= h7;
                    h7 = h7 << 57 | h7 >> -57;
                    h6 += h8;
                    h8 += p64[8];
                    h10 ^= h6;
                    h7 ^= h8;
                    h8 = h8 << 55 | h8 >> -55;
                    h7 += h9;
                    h9 += p64[9];
                    h11 ^= h7;
                    h8 ^= h9;
                    h9 = h9 << 54 | h9 >> -54;
                    h8 += h10;
                    h10 += p64[10];
                    h0 ^= h8;
                    h9 ^= h10;
                    h10 = h10 << 22 | h10 >> -22;
                    h9 += h11;
                    h11 += p64[11];
                    h1 ^= h9;
                    h10 ^= h11;
                    h11 = h11 << 46 | h11 >> -46;
                    h10 += h0;
                    p64 += NumVars;
                }
            }
            else
            {
                while (p64 < end)
                {
                    MemCpy(buf, p64, BlockSize);

                    h0 += buf[0];
                    h2 ^= h10;
                    h11 ^= h0;
                    h0 = h0 << 11 | h0 >> -11;
                    h11 += h1;
                    h1 += buf[1];
                    h3 ^= h11;
                    h0 ^= h1;
                    h1 = h1 << 32 | h1 >> 32;
                    h0 += h2;
                    h2 += buf[2];
                    h4 ^= h0;
                    h1 ^= h2;
                    h2 = h2 << 43 | h2 >> -43;
                    h1 += h3;
                    h3 += buf[3];
                    h5 ^= h1;
                    h2 ^= h3;
                    h3 = h3 << 31 | h3 >> -31;
                    h2 += h4;
                    h4 += buf[4];
                    h6 ^= h2;
                    h3 ^= h4;
                    h4 = h4 << 17 | h4 >> -17;
                    h3 += h5;
                    h5 += buf[5];
                    h7 ^= h3;
                    h4 ^= h5;
                    h5 = h5 << 28 | h5 >> -28;
                    h4 += h6;
                    h6 += buf[6];
                    h8 ^= h4;
                    h5 ^= h6;
                    h6 = h6 << 39 | h6 >> -39;
                    h5 += h7;
                    h7 += buf[7];
                    h9 ^= h5;
                    h6 ^= h7;
                    h7 = h7 << 57 | h7 >> -57;
                    h6 += h8;
                    h8 += buf[8];
                    h10 ^= h6;
                    h7 ^= h8;
                    h8 = h8 << 55 | h8 >> -55;
                    h7 += h9;
                    h9 += buf[9];
                    h11 ^= h7;
                    h8 ^= h9;
                    h9 = h9 << 54 | h9 >> -54;
                    h8 += h10;
                    h10 += buf[10];
                    h0 ^= h8;
                    h9 ^= h10;
                    h10 = h10 << 22 | h10 >> -22;
                    h9 += h11;
                    h11 += buf[11];
                    h1 ^= h9;
                    h10 ^= h11;
                    h11 = h11 << 46 | h11 >> -46;
                    h10 += h0;
                    p64 += NumVars;
                }
            }

            int remainder = length - (int)((byte*)end - (byte*)message);
            if (remainder != 0)
            {
                MemCpy(buf, end, remainder);
            }

            MemZero((byte*)buf + remainder, BlockSize - remainder);
            ((byte*)buf)[BlockSize - 1] = (byte)remainder;

            h0 += buf[0];
            h1 += buf[1];
            h2 += buf[2];
            h3 += buf[3];
            h4 += buf[4];
            h5 += buf[5];
            h6 += buf[6];
            h7 += buf[7];
            h8 += buf[8];
            h9 += buf[9];
            h10 += buf[10];
            h11 += buf[11];
            h11 += h1;
            h2 ^= h11;
            h1 = h1 << 44 | h1 >> -44;
            h0 += h2;
            h3 ^= h0;
            h2 = h2 << 15 | h2 >> -15;
            h1 += h3;
            h4 ^= h1;
            h3 = h3 << 34 | h3 >> -34;
            h2 += h4;
            h5 ^= h2;
            h4 = h4 << 21 | h4 >> -21;
            h3 += h5;
            h6 ^= h3;
            h5 = h5 << 38 | h5 >> -38;
            h4 += h6;
            h7 ^= h4;
            h6 = h6 << 33 | h6 >> -33;
            h5 += h7;
            h8 ^= h5;
            h7 = h7 << 10 | h7 >> -10;
            h6 += h8;
            h9 ^= h6;
            h8 = h8 << 13 | h8 >> -13;
            h7 += h9;
            h10 ^= h7;
            h9 = h9 << 38 | h9 >> -38;
            h8 += h10;
            h11 ^= h8;
            h10 = h10 << 53 | h10 >> -53;
            h9 += h11;
            h0 ^= h9;
            h11 = h11 << 42 | h11 >> -42;
            h10 += h0;
            h1 ^= h10;
            h0 = h0 << 54 | h0 >> -54;
            h11 += h1;
            h2 ^= h11;
            h1 = h1 << 44 | h1 >> -44;
            h0 += h2;
            h3 ^= h0;
            h2 = h2 << 15 | h2 >> -15;
            h1 += h3;
            h4 ^= h1;
            h3 = h3 << 34 | h3 >> -34;
            h2 += h4;
            h5 ^= h2;
            h4 = h4 << 21 | h4 >> -21;
            h3 += h5;
            h6 ^= h3;
            h5 = h5 << 38 | h5 >> -38;
            h4 += h6;
            h7 ^= h4;
            h6 = h6 << 33 | h6 >> -33;
            h5 += h7;
            h8 ^= h5;
            h7 = h7 << 10 | h7 >> -10;
            h6 += h8;
            h9 ^= h6;
            h8 = h8 << 13 | h8 >> -13;
            h7 += h9;
            h10 ^= h7;
            h9 = h9 << 38 | h9 >> -38;
            h8 += h10;
            h11 ^= h8;
            h10 = h10 << 53 | h10 >> -53;
            h9 += h11;
            h0 ^= h9;
            h11 = h11 << 42 | h11 >> -42;
            h10 += h0;
            h1 ^= h10;
            h0 = h0 << 54 | h0 >> -54;
            h11 += h1;
            h2 ^= h11;
            h1 = h1 << 44 | h1 >> -44;
            h0 += h2;
            h3 ^= h0;
            h2 = h2 << 15 | h2 >> -15;
            h1 += h3;
            h4 ^= h1;
            h3 = h3 << 34 | h3 >> -34;
            h2 += h4;
            h5 ^= h2;
            h4 = h4 << 21 | h4 >> -21;
            h3 += h5;
            h6 ^= h3;
            h5 = h5 << 38 | h5 >> -38;
            h4 += h6;
            h7 ^= h4;
            h6 = h6 << 33 | h6 >> -33;
            h5 += h7;
            h8 ^= h5;
            h7 = h7 << 10 | h7 >> -10;
            h6 += h8;
            h9 ^= h6;
            h8 = h8 << 13 | h8 >> -13;
            h7 += h9;
            h10 ^= h7;
            h9 = h9 << 38 | h9 >> -38;
            h8 += h10;
            h11 ^= h8;
            h10 = h10 << 53 | h10 >> -53;
            h9 += h11;
            h0 ^= h9;
            h10 += h0;
            h1 ^= h10;
            h0 = h0 << 54 | h0 >> -54;
            hash2 = h1;
            hash1 = h0;
        }

        /// <summary>Calculates the 64-bit SpookyHash for a message.</summary>
        /// <returns>The 64-bit hash.</returns>
        /// <param name="message">Pointer to the first element to hash.</param>
        /// <param name="length">The size, in bytes, of the elements to hash.</param>
        /// <param name="seed">A seed for the hash.</param>
        /// <exception cref="AccessViolationException">This is an unsafe method. If you attempt to read past the buffer
        /// that <paramref name="message"/> points too, you may raise an <see cref="AccessViolationException"/>, or you
        /// may have incorrect results.</exception>
        [CLSCompliant(false), SecurityCritical]
        public static unsafe ulong Hash64(void* message, int length, ulong seed)
        {
            Hash128(message, length, ref seed, ref seed);
            return seed;
        }

        /// <summary>Calculates a 32-bit SpookyHash for a message.</summary>
        /// <returns>The 32-bit hash.</returns>
        /// <param name="message">Pointer to the first element to hash.</param>
        /// <param name="length">The size, in bytes, of the elements to hash.</param>
        /// <param name="seed">A seed for the hash.</param>
        /// <exception cref="AccessViolationException">This is an unsafe method. If you attempt to read past the buffer
        /// that <paramref name="message"/> points too, you may raise an <see cref="AccessViolationException"/>, or you
        /// may have incorrect results.</exception>
        [CLSCompliant(false), SecurityCritical]
        public static unsafe uint Hash32(void* message, int length, uint seed) => (uint)Hash64(message, length, seed);

        [SecurityCritical]
        private static unsafe void Short(void* message, int length, ref ulong hash1, ref ulong hash2)
        {
            if (!AllowUnalignedRead && length != 0 && ((long)message & 7) != 0)
            {
                ulong* buf = stackalloc ulong[2 * NumVars];
                MemCpy(buf, message, length);
                message = buf;
            }

            ulong* p64 = (ulong*)message;

            int remainder = length & 31;
            ulong a = hash1;
            ulong b = hash2;
            ulong c = SpookyConst;
            ulong d = SpookyConst;

            if (length > 15)
            {
                ulong* end = p64 + length / 32 * 4;
                for (; p64 < end; p64 += 4)
                {
                    c += p64[0];
                    d += p64[1];
                    c = c << 50 | c >> -50;
                    c += d;
                    a ^= c;
                    d = d << 52 | d >> -52;
                    d += a;
                    b ^= d;
                    a = a << 30 | a >> -30;
                    a += b;
                    c ^= a;
                    b = b << 41 | b >> -41;
                    b += c;
                    d ^= b;
                    c = c << 54 | c >> -54;
                    c += d;
                    a ^= c;
                    d = d << 48 | d >> -48;
                    d += a;
                    b ^= d;
                    a = a << 38 | a >> -38;
                    a += b;
                    c ^= a;
                    b = b << 37 | b >> -37;
                    b += c;
                    d ^= b;
                    c = c << 62 | c >> -62;
                    c += d;
                    a ^= c;
                    d = d << 34 | d >> -34;
                    d += a;
                    b ^= d;
                    a = a << 5 | a >> -5;
                    a += b;
                    c ^= a;
                    b = b << 36 | b >> -36;
                    b += c;
                    d ^= b;
                    a += p64[2];
                    b += p64[3];
                }

                if (remainder >= 16)
                {
                    c += p64[0];
                    d += p64[1];
                    c = c << 50 | c >> -50;
                    c += d;
                    a ^= c;
                    d = d << 52 | d >> -52;
                    d += a;
                    b ^= d;
                    a = a << 30 | a >> -30;
                    a += b;
                    c ^= a;
                    b = b << 41 | b >> -41;
                    b += c;
                    d ^= b;
                    c = c << 54 | c >> -54;
                    c += d;
                    a ^= c;
                    d = d << 48 | d >> -48;
                    d += a;
                    b ^= d;
                    a = a << 38 | a >> -38;
                    a += b;
                    c ^= a;
                    b = b << 37 | b >> -37;
                    b += c;
                    d ^= b;
                    c = c << 62 | c >> -62;
                    c += d;
                    a ^= c;
                    d = d << 34 | d >> -34;
                    d += a;
                    b ^= d;
                    a = a << 5 | a >> -5;
                    a += b;
                    c ^= a;
                    b = b << 36 | b >> -36;
                    b += c;
                    d ^= b;
                    p64 += 2;
                    remainder -= 16;
                }
            }

            d += (ulong)length << 56;
            switch (remainder)
            {
                case 15:
                    d += (ulong)((byte*)p64)[14] << 48;
                    goto case 14;
                case 14:
                    d += (ulong)((byte*)p64)[13] << 40;
                    goto case 13;
                case 13:
                    d += (ulong)((byte*)p64)[12] << 32;
                    goto case 12;
                case 12:
                    d += ((uint*)p64)[2];
                    c += p64[0];
                    break;
                case 11:
                    d += (ulong)((byte*)p64)[10] << 16;
                    goto case 10;
                case 10:
                    d += (ulong)((byte*)p64)[9] << 8;
                    goto case 9;
                case 9:
                    d += ((byte*)p64)[8];
                    goto case 8;
                case 8:
                    c += p64[0];
                    break;
                case 7:
                    c += (ulong)((byte*)p64)[6] << 48;
                    goto case 6;
                case 6:
                    c += (ulong)((byte*)p64)[5] << 40;
                    goto case 5;
                case 5:
                    c += (ulong)((byte*)p64)[4] << 32;
                    goto case 4;
                case 4:
                    c += ((uint*)p64)[0];
                    break;
                case 3:
                    c += (ulong)((byte*)p64)[2] << 16;
                    goto case 2;
                case 2:
                    c += (ulong)((byte*)p64)[1] << 8;
                    goto case 1;
                case 1:
                    c += ((byte*)p64)[0];
                    break;
                case 0:
                    c += SpookyConst;
                    d += SpookyConst;
                    break;
            }

            d ^= c;
            c = c << 15 | c >> -15;
            d += c;
            a ^= d;
            d = d << 52 | d >> -52;
            a += d;
            b ^= a;
            a = a << 26 | a >> -26;
            b += a;
            c ^= b;
            b = b << 51 | b >> -51;
            c += b;
            d ^= c;
            c = c << 28 | c >> -28;
            d += c;
            a ^= d;
            d = d << 9 | d >> -9;
            a += d;
            b ^= a;
            a = a << 47 | a >> -47;
            b += a;
            c ^= b;
            b = b << 54 | b >> -54;
            c += b;
            d ^= c;
            c = c << 32 | c >> -32;
            d += c;
            a ^= d;
            d = d << 25 | d >> -25;
            a += d;
            b ^= a;
            a = a << 63 | a >> -63;
            b += a;
            hash2 = b;
            hash1 = a;
        }

        private readonly ulong[] _data = new ulong[2 * NumVars];

        private ulong _state0,
            _state1,
            _state2,
            _state3,
            _state4,
            _state5,
            _state6,
            _state7,
            _state8,
            _state9,
            _state10,
            _state11;

        private int _length;
        private int _remainder;

        /// <summary>Initialises a new instance of the <see cref="SpookyHash"/> class with a default seed
        /// value.</summary>
        public SpookyHash()
            : this(SpookyConst, SpookyConst)
        {
        }

        private SpookyHash(SerializationInfo info, StreamingContext context)
        {
            _data = (ulong[])info.GetValue("d", typeof(ulong[]));
            _state0 = info.GetUInt64("s0");
            _state1 = info.GetUInt64("s1");
            _state2 = info.GetUInt64("s2");
            _state3 = info.GetUInt64("s3");
            _state4 = info.GetUInt64("s4");
            _state5 = info.GetUInt64("s5");
            _state6 = info.GetUInt64("s6");
            _state7 = info.GetUInt64("s7");
            _state8 = info.GetUInt64("s8");
            _state9 = info.GetUInt64("s9");
            _state10 = info.GetUInt64("s10");
            _state11 = info.GetUInt64("s11");
            _length = info.GetInt32("l");
            _remainder = info.GetInt32("r");
        }

        /// <summary>Initialises a new instance of the <see cref="SpookyHash"/> class.</summary>
        /// <param name="seed1">First half of a 128-bit seed for the hash.</param>
        /// <param name="seed2">Second half of a 128-bit seed for the hash.</param>
        /// <remarks>This is not a CLS-compliant method, and is not accessible by some .NET languages.</remarks>
        [CLSCompliant(false)]
        public SpookyHash(ulong seed1, ulong seed2) => Init(seed1, seed2);

        /// <summary>Initialises a new instance of the <see cref="SpookyHash"/> class.</summary>
        /// <param name="seed1">First half of a 128-bit seed for the hash.</param>
        /// <param name="seed2">Second half of a 128-bit seed for the hash.</param>
        public SpookyHash(long seed1, long seed2) => Init(seed1, seed2);

        /// <summary>Re-initialise the <see cref="SpookyHash"/> object with the specified seed.</summary>
        /// <param name="seed1">First half of a 128-bit seed for the hash.</param>
        /// <param name="seed2">Second half of a 128-bit seed for the hash.</param>
        /// <remarks>This is not a CLS-compliant method, and is not accessible by some .NET languages.</remarks>
        [CLSCompliant(false)]
        public void Init(ulong seed1, ulong seed2)
        {
            _length = _remainder = 0;
            _state0 = seed1;
            _state1 = seed2;
        }

        /// <summary>Re-initialise the <see cref="SpookyHash"/> object with the specified seed.</summary>
        /// <param name="seed1">First half of a 128-bit seed for the hash.</param>
        /// <param name="seed2">Second half of a 128-bit seed for the hash.</param>
        public void Init(long seed1, long seed2) => Init((ulong)seed1, (ulong)seed2);

        /// <summary>Updates the in-progress hash generation with more of the message.</summary>
        /// <param name="message">String to hash.</param>
        /// <param name="startIndex">Start index in the string, from which to hash.</param>
        /// <param name="length">How many characters to hash.</param>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> was null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="startIndex"/> is less than zero, or greater
        /// than the length of the array.</exception>
        /// <exception cref="ArgumentException"><paramref name="startIndex"/> plus <paramref name="length"/> is greater
        /// than the length of the array.</exception>
        [SecuritySafeCritical]
        public unsafe void Update(string message, int startIndex, int length)
        {
            ExceptionHelper.CheckString(message, startIndex, length);
            fixed (char* ptr = message)
            {
                Update(ptr + startIndex, length << 1);
            }
        }

        /// <summary>Updates the in-progress hash generation with more of the message.</summary>
        /// <param name="message">String to hash.</param>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> was null.</exception>
        public void Update(string message)
        {
            ExceptionHelper.CheckMessageNotNull(message);
            Update(message, 0, message.Length);
        }

        /// <summary>Updates the in-progress hash generation with each <see cref="string"/> in an sequence of strings.
        /// </summary>
        /// <param name="message">The sequence of <see cref="string"/>s to hash.</param>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> was null.</exception>
        /// <remarks>It is acceptable for strings within the sequence to be <see langword="null"/>. They will affect the
        /// hash produced. i.e. the sequence <c>{"a", "b", "c"}</c> will produce a different hash than
        /// <c>{"a", null, "b", "c"}</c>. This is often useful, but if this is undesirable in a given case (you want the
        /// same hash as the concatenation), then filter out <see langword="null"/> <see cref="string"/>s first.
        /// </remarks>
        [SecuritySafeCritical]
        public unsafe void Update(IEnumerable<string> message)
        {
            ExceptionHelper.CheckMessageNotNull(message);
            foreach (string item in message)
            {
                if (item == null)
                {
                    Update(SpookyConst); // Just to make sure we produce a different hash for this case.
                }
                else
                {
                    fixed (char* ptr = item)
                    {
                        Update(ptr, item.Length << 1);
                    }
                }
            }
        }

        /// <summary>Updates the in-progress hash generation with more of the message.</summary>
        /// <param name="message">Pointer to the data to hash.</param>
        /// <param name="length">How many bytes to hash.</param>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> was a null pointer.</exception>
        /// <exception cref="AccessViolationException">This is an unsafe method. If you attempt to read past the buffer
        /// that <paramref name="message"/> points too, you may raise an <see cref="AccessViolationException"/>, or you
        /// may have incorrect results.</exception>
        [CLSCompliant(false), SecurityCritical]
        public unsafe void Update(void* message, int length)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            if (length == 0)
            {
                return;
            }

            ulong h0, h1, h2, h3, h4, h5, h6, h7, h8, h9, h10, h11;
            int newLength = length + _remainder;
            if (newLength < BufSize)
            {
                fixed (ulong* uptr = _data)
                {
                    MemCpy((byte*)uptr + _remainder, message, length);
                }

                _length = length + _length;
                _remainder = newLength;
                return;
            }

            if (_length < BufSize)
            {
                h0 = h3 = h6 = h9 = _state0;
                h1 = h4 = h7 = h10 = _state1;
                h2 = h5 = h8 = h11 = SpookyConst;
            }
            else
            {
                h0 = _state0;
                h1 = _state1;
                h2 = _state2;
                h3 = _state3;
                h4 = _state4;
                h5 = _state5;
                h6 = _state6;
                h7 = _state7;
                h8 = _state8;
                h9 = _state9;
                h10 = _state10;
                h11 = _state11;
            }

            _length += length;

            fixed (ulong* p64Fixed = _data)
            {
                ulong* p64;
                if (_remainder != 0)
                {
                    int prefix = BufSize - _remainder;
                    MemCpy((byte*)p64Fixed + _remainder, message, prefix);

                    h0 += p64Fixed[0];
                    h2 ^= h10;
                    h11 ^= h0;
                    h0 = h0 << 11 | h0 >> -11;
                    h11 += h1;
                    h1 += p64Fixed[1];
                    h3 ^= h11;
                    h0 ^= h1;
                    h1 = h1 << 32 | h1 >> -32;
                    h0 += h2;
                    h2 += p64Fixed[2];
                    h4 ^= h0;
                    h1 ^= h2;
                    h2 = h2 << 43 | h2 >> -43;
                    h1 += h3;
                    h3 += p64Fixed[3];
                    h5 ^= h1;
                    h2 ^= h3;
                    h3 = h3 << 31 | h3 >> -31;
                    h2 += h4;
                    h4 += p64Fixed[4];
                    h6 ^= h2;
                    h3 ^= h4;
                    h4 = h4 << 17 | h4 >> -17;
                    h3 += h5;
                    h5 += p64Fixed[5];
                    h7 ^= h3;
                    h4 ^= h5;
                    h5 = h5 << 28 | h5 >> -28;
                    h4 += h6;
                    h6 += p64Fixed[6];
                    h8 ^= h4;
                    h5 ^= h6;
                    h6 = h6 << 39 | h6 >> -39;
                    h5 += h7;
                    h7 += p64Fixed[7];
                    h9 ^= h5;
                    h6 ^= h7;
                    h7 = h7 << 57 | h7 >> -57;
                    h6 += h8;
                    h8 += p64Fixed[8];
                    h10 ^= h6;
                    h7 ^= h8;
                    h8 = h8 << 55 | h8 >> -55;
                    h7 += h9;
                    h9 += p64Fixed[9];
                    h11 ^= h7;
                    h8 ^= h9;
                    h9 = h9 << 54 | h9 >> -54;
                    h8 += h10;
                    h10 += p64Fixed[10];
                    h0 ^= h8;
                    h9 ^= h10;
                    h10 = h10 << 22 | h10 >> -22;
                    h9 += h11;
                    h11 += p64Fixed[11];
                    h1 ^= h9;
                    h10 ^= h11;
                    h11 = h11 << 46 | h11 >> -46;
                    h10 += h0;
                    p64 = p64Fixed + NumVars;
                    h0 += p64[0];
                    h2 ^= h10;
                    h11 ^= h0;
                    h0 = h0 << 11 | h0 >> -11;
                    h11 += h1;
                    h1 += p64[1];
                    h3 ^= h11;
                    h0 ^= h1;
                    h1 = h1 << 32 | h1 >> -32;
                    h0 += h2;
                    h2 += p64[2];
                    h4 ^= h0;
                    h1 ^= h2;
                    h2 = h2 << 43 | h2 >> -43;
                    h1 += h3;
                    h3 += p64[3];
                    h5 ^= h1;
                    h2 ^= h3;
                    h3 = h3 << 31 | h3 >> -31;
                    h2 += h4;
                    h4 += p64[4];
                    h6 ^= h2;
                    h3 ^= h4;
                    h4 = h4 << 17 | h4 >> -17;
                    h3 += h5;
                    h5 += p64[5];
                    h7 ^= h3;
                    h4 ^= h5;
                    h5 = h5 << 28 | h5 >> -28;
                    h4 += h6;
                    h6 += p64[6];
                    h8 ^= h4;
                    h5 ^= h6;
                    h6 = h6 << 39 | h6 >> -39;
                    h5 += h7;
                    h7 += p64[7];
                    h9 ^= h5;
                    h6 ^= h7;
                    h7 = h7 << 57 | h7 >> -57;
                    h6 += h8;
                    h8 += p64[8];
                    h10 ^= h6;
                    h7 ^= h8;
                    h8 = h8 << 55 | h8 >> -55;
                    h7 += h9;
                    h9 += p64[9];
                    h11 ^= h7;
                    h8 ^= h9;
                    h9 = h9 << 54 | h9 >> -54;
                    h8 += h10;
                    h10 += p64[10];
                    h0 ^= h8;
                    h9 ^= h10;
                    h10 = h10 << 22 | h10 >> -22;
                    h9 += h11;
                    h11 += p64[11];
                    h1 ^= h9;
                    h10 ^= h11;
                    h11 = h11 << 46 | h11 >> -46;
                    h10 += h0;
                    p64 = (ulong*)((byte*)message + prefix);
                    length -= prefix;
                }
                else
                {
                    p64 = (ulong*)message;
                }

                ulong* end = p64 + length / BlockSize * NumVars;
                byte remainder = (byte)(length - ((byte*)end - (byte*)p64));
                if (AllowUnalignedRead || ((long)message & 7) == 0)
                {
                    while (p64 < end)
                    {
                        h0 += p64[0];
                        h2 ^= h10;
                        h11 ^= h0;
                        h0 = h0 << 11 | h0 >> -11;
                        h11 += h1;
                        h1 += p64[1];
                        h3 ^= h11;
                        h0 ^= h1;
                        h1 = h1 << 32 | h1 >> -32;
                        h0 += h2;
                        h2 += p64[2];
                        h4 ^= h0;
                        h1 ^= h2;
                        h2 = h2 << 43 | h2 >> -43;
                        h1 += h3;
                        h3 += p64[3];
                        h5 ^= h1;
                        h2 ^= h3;
                        h3 = h3 << 31 | h3 >> -31;
                        h2 += h4;
                        h4 += p64[4];
                        h6 ^= h2;
                        h3 ^= h4;
                        h4 = h4 << 17 | h4 >> -17;
                        h3 += h5;
                        h5 += p64[5];
                        h7 ^= h3;
                        h4 ^= h5;
                        h5 = h5 << 28 | h5 >> -28;
                        h4 += h6;
                        h6 += p64[6];
                        h8 ^= h4;
                        h5 ^= h6;
                        h6 = h6 << 39 | h6 >> -39;
                        h5 += h7;
                        h7 += p64[7];
                        h9 ^= h5;
                        h6 ^= h7;
                        h7 = h7 << 57 | h7 >> -57;
                        h6 += h8;
                        h8 += p64[8];
                        h10 ^= h6;
                        h7 ^= h8;
                        h8 = h8 << 55 | h8 >> -55;
                        h7 += h9;
                        h9 += p64[9];
                        h11 ^= h7;
                        h8 ^= h9;
                        h9 = h9 << 54 | h9 >> -54;
                        h8 += h10;
                        h10 += p64[10];
                        h0 ^= h8;
                        h9 ^= h10;
                        h10 = h10 << 22 | h10 >> -22;
                        h9 += h11;
                        h11 += p64[11];
                        h1 ^= h9;
                        h10 ^= h11;
                        h11 = h11 << 46 | h11 >> -46;
                        h10 += h0;
                        p64 += NumVars;
                    }
                }
                else
                {
                    fixed (ulong* dataPtr = _data)
                    {
                        while (p64 < end)
                        {
                            MemCpy(dataPtr, p64, BlockSize);
                            h0 += _data[0];
                            h2 ^= h10;
                            h11 ^= h0;
                            h0 = h0 << 11 | h0 >> -11;
                            h11 += h1;
                            h1 += _data[1];
                            h3 ^= h11;
                            h0 ^= h1;
                            h1 = h1 << 32 | h1 >> -32;
                            h0 += h2;
                            h2 += _data[2];
                            h4 ^= h0;
                            h1 ^= h2;
                            h2 = h2 << 43 | h2 >> -43;
                            h1 += h3;
                            h3 += _data[3];
                            h5 ^= h1;
                            h2 ^= h3;
                            h3 = h3 << 31 | h3 >> -31;
                            h2 += h4;
                            h4 += _data[4];
                            h6 ^= h2;
                            h3 ^= h4;
                            h4 = h4 << 17 | h4 >> -17;
                            h3 += h5;
                            h5 += _data[5];
                            h7 ^= h3;
                            h4 ^= h5;
                            h5 = h5 << 28 | h5 >> -28;
                            h4 += h6;
                            h6 += _data[6];
                            h8 ^= h4;
                            h5 ^= h6;
                            h6 = h6 << 39 | h6 >> -39;
                            h5 += h7;
                            h7 += _data[7];
                            h9 ^= h5;
                            h6 ^= h7;
                            h7 = h7 << 57 | h7 >> -57;
                            h6 += h8;
                            h8 += _data[8];
                            h10 ^= h6;
                            h7 ^= h8;
                            h8 = h8 << 55 | h8 >> -55;
                            h7 += h9;
                            h9 += _data[9];
                            h11 ^= h7;
                            h8 ^= h9;
                            h9 = h9 << 54 | h9 >> -54;
                            h8 += h10;
                            h10 += _data[10];
                            h0 ^= h8;
                            h9 ^= h10;
                            h10 = h10 << 22 | h10 >> -22;
                            h9 += h11;
                            h11 += _data[11];
                            h1 ^= h9;
                            h10 ^= h11;
                            h11 = h11 << 46 | h11 >> -46;
                            h10 += h0;
                            p64 += NumVars;
                        }
                    }
                }

                _remainder = remainder;
                if (remainder != 0)
                {
                    MemCpy(p64Fixed, end, remainder);
                }
            }

            _state0 = h0;
            _state1 = h1;
            _state2 = h2;
            _state3 = h3;
            _state4 = h4;
            _state5 = h5;
            _state6 = h6;
            _state7 = h7;
            _state8 = h8;
            _state9 = h9;
            _state10 = h10;
            _state11 = h11;
        }

        /// <summary>Produces the final hash of the message. It does not prevent further updates, and can be called
        /// multiple times while the hash is added to.</summary>
        /// <param name="hash1">The first half of the 128-bit hash.</param>
        /// <param name="hash2">The second half of the 128-bit hash.</param>
        public void Final(out long hash1, out long hash2)
        {
            Final(out ulong uhash1, out ulong uhash2);
            hash1 = (long)uhash1;
            hash2 = (long)uhash2;
        }

        /// <summary>Produces the final hash of the message. It does not prevent further updates, and can be called
        /// multiple times while the hash is added to.</summary>
        /// <returns>A <see cref="HashCode128"/> representing the 128-bit hash.</returns>
        public HashCode128 Final()
        {
            Final(out ulong hash1, out ulong hash2);
            return new HashCode128(hash1, hash2);
        }

        /// <summary>Produces the final hash of the message. It does not prevent further updates, and can be called
        /// multiple times while the hash is added to.</summary>
        /// <param name="hash1">The first half of the 128-bit hash.</param>
        /// <param name="hash2">The second half of the 128-bit hash.</param>
        /// <remarks>This is not a CLS-compliant method, and is not accessible by some .NET languages.</remarks>
        [CLSCompliant(false)]
        [SecuritySafeCritical]
        public unsafe void Final(out ulong hash1, out ulong hash2)
        {
            if (_length < BufSize)
            {
                hash1 = _state0;
                hash2 = _state1;
                fixed (void* ptr = _data)
                {
                    Short(ptr, _length, ref hash1, ref hash2);
                }

                return;
            }

            ulong h0 = _state0;
            ulong h1 = _state1;
            ulong h2 = _state2;
            ulong h3 = _state3;
            ulong h4 = _state4;
            ulong h5 = _state5;
            ulong h6 = _state6;
            ulong h7 = _state7;
            ulong h8 = _state8;
            ulong h9 = _state9;
            ulong h10 = _state10;
            ulong h11 = _state11;
            fixed (ulong* dataFixed = _data)
            {
                ulong* data = dataFixed;
                int remainder = _remainder;
                if (remainder >= BlockSize)
                {
                    h0 += data[0];
                    h2 ^= h10;
                    h11 ^= h0;
                    h0 = h0 << 11 | h0 >> -11;
                    h11 += h1;
                    h1 += data[1];
                    h3 ^= h11;
                    h0 ^= h1;
                    h1 = h1 << 32 | h1 >> -32;
                    h0 += h2;
                    h2 += data[2];
                    h4 ^= h0;
                    h1 ^= h2;
                    h2 = h2 << 43 | h2 >> -43;
                    h1 += h3;
                    h3 += data[3];
                    h5 ^= h1;
                    h2 ^= h3;
                    h3 = h3 << 31 | h3 >> -31;
                    h2 += h4;
                    h4 += data[4];
                    h6 ^= h2;
                    h3 ^= h4;
                    h4 = h4 << 17 | h4 >> -17;
                    h3 += h5;
                    h5 += data[5];
                    h7 ^= h3;
                    h4 ^= h5;
                    h5 = h5 << 28 | h5 >> -28;
                    h4 += h6;
                    h6 += data[6];
                    h8 ^= h4;
                    h5 ^= h6;
                    h6 = h6 << 39 | h6 >> -39;
                    h5 += h7;
                    h7 += data[7];
                    h9 ^= h5;
                    h6 ^= h7;
                    h7 = h7 << 57 | h7 >> -57;
                    h6 += h8;
                    h8 += data[8];
                    h10 ^= h6;
                    h7 ^= h8;
                    h8 = h8 << 55 | h8 >> -55;
                    h7 += h9;
                    h9 += data[9];
                    h11 ^= h7;
                    h8 ^= h9;
                    h9 = h9 << 54 | h9 >> -54;
                    h8 += h10;
                    h10 += data[10];
                    h0 ^= h8;
                    h9 ^= h10;
                    h10 = h10 << 22 | h10 >> -22;
                    h9 += h11;
                    h11 += data[11];
                    h1 ^= h9;
                    h10 ^= h11;
                    h11 = h11 << 46 | h11 >> -46;
                    h10 += h0;
                    data += NumVars;
                    remainder = remainder - BlockSize;
                }
                MemZero((byte*)data + remainder, BlockSize - remainder);
                *((byte*)data + BlockSize - 1) = (byte)remainder;
                h0 += data[0];
                h1 += data[1];
                h2 += data[2];
                h3 += data[3];
                h4 += data[4];
                h5 += data[5];
                h6 += data[6];
                h7 += data[7];
                h8 += data[8];
                h9 += data[9];
                h10 += data[10];
                h11 += data[11];
            }
            h11 += h1;
            h2 ^= h11;
            h1 = h1 << 44 | h1 >> -44;
            h0 += h2;
            h3 ^= h0;
            h2 = h2 << 15 | h2 >> -15;
            h1 += h3;
            h4 ^= h1;
            h3 = h3 << 34 | h3 >> -34;
            h2 += h4;
            h5 ^= h2;
            h4 = h4 << 21 | h4 >> -21;
            h3 += h5;
            h6 ^= h3;
            h5 = h5 << 38 | h5 >> -38;
            h4 += h6;
            h7 ^= h4;
            h6 = h6 << 33 | h6 >> -33;
            h5 += h7;
            h8 ^= h5;
            h7 = h7 << 10 | h7 >> -10;
            h6 += h8;
            h9 ^= h6;
            h8 = h8 << 13 | h8 >> -13;
            h7 += h9;
            h10 ^= h7;
            h9 = h9 << 38 | h9 >> -38;
            h8 += h10;
            h11 ^= h8;
            h10 = h10 << 53 | h10 >> -53;
            h9 += h11;
            h0 ^= h9;
            h11 = h11 << 42 | h11 >> -42;
            h10 += h0;
            h1 ^= h10;
            h0 = h0 << 54 | h0 >> -54;
            h11 += h1;
            h2 ^= h11;
            h1 = h1 << 44 | h1 >> -44;
            h0 += h2;
            h3 ^= h0;
            h2 = h2 << 15 | h2 >> -15;
            h1 += h3;
            h4 ^= h1;
            h3 = h3 << 34 | h3 >> -34;
            h2 += h4;
            h5 ^= h2;
            h4 = h4 << 21 | h4 >> -21;
            h3 += h5;
            h6 ^= h3;
            h5 = h5 << 38 | h5 >> -38;
            h4 += h6;
            h7 ^= h4;
            h6 = h6 << 33 | h6 >> -33;
            h5 += h7;
            h8 ^= h5;
            h7 = h7 << 10 | h7 >> -10;
            h6 += h8;
            h9 ^= h6;
            h8 = h8 << 13 | h8 >> -13;
            h7 += h9;
            h10 ^= h7;
            h9 = h9 << 38 | h9 >> -38;
            h8 += h10;
            h11 ^= h8;
            h10 = h10 << 53 | h10 >> -53;
            h9 += h11;
            h0 ^= h9;
            h11 = h11 << 42 | h11 >> -42;
            h10 += h0;
            h1 ^= h10;
            h0 = h0 << 54 | h0 >> -54;
            h11 += h1;
            h2 ^= h11;
            h1 = h1 << 44 | h1 >> -44;
            h0 += h2;
            h3 ^= h0;
            h2 = h2 << 15 | h2 >> -15;
            h1 += h3;
            h4 ^= h1;
            h3 = h3 << 34 | h3 >> -34;
            h2 += h4;
            h5 ^= h2;
            h4 = h4 << 21 | h4 >> -21;
            h3 += h5;
            h6 ^= h3;
            h5 = h5 << 38 | h5 >> -38;
            h4 += h6;
            h7 ^= h4;
            h6 = h6 << 33 | h6 >> -33;
            h5 += h7;
            h8 ^= h5;
            h7 = h7 << 10 | h7 >> -10;
            h6 += h8;
            h9 ^= h6;
            h8 = h8 << 13 | h8 >> -13;
            h7 += h9;
            h10 ^= h7;
            h9 = h9 << 38 | h9 >> -38;
            h8 += h10;
            h11 ^= h8;
            h10 = h10 << 53 | h10 >> -53;
            h9 += h11;
            h0 ^= h9;
            h10 += h0;
            h1 ^= h10;
            h0 = h0 << 54 | h0 >> -54;
            hash2 = h1;
            hash1 = h0;
        }

        [SecurityPermission(SecurityAction.LinkDemand, Flags = SecurityPermissionFlag.SerializationFormatter)]
        [SecurityCritical]
        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            ExceptionHelper.CheckNotNull(info, "info");
            info.AddValue("d", _data);
            info.AddValue("s0", _state0);
            info.AddValue("s1", _state1);
            info.AddValue("s2", _state2);
            info.AddValue("s3", _state3);
            info.AddValue("s4", _state4);
            info.AddValue("s5", _state5);
            info.AddValue("s6", _state6);
            info.AddValue("s7", _state7);
            info.AddValue("s8", _state8);
            info.AddValue("s9", _state9);
            info.AddValue("s10", _state10);
            info.AddValue("s11", _state11);
            info.AddValue("l", _length);
            info.AddValue("r", _remainder);

        }
        [SecurityCritical]
        [ExcludeFromCodeCoverage]
        private static unsafe void MemCpy4ByteAligned(void* dest, void* source, long length)
        {
            if (length >= 4)
            {
                var di = (int*)dest;
                var si = (int*)source;
                long rem = length >> 2;
                length -= rem << 2;
                switch (rem & 15)
                {
                    case 0:
                        *di++ = *si++;
                        goto case 15;
                    case 15:
                        *di++ = *si++;
                        goto case 14;
                    case 14:
                        *di++ = *si++;
                        goto case 13;
                    case 13:
                        *di++ = *si++;
                        goto case 12;
                    case 12:
                        *di++ = *si++;
                        goto case 11;
                    case 11:
                        *di++ = *si++;
                        goto case 10;
                    case 10:
                        *di++ = *si++;
                        goto case 9;
                    case 9:
                        *di++ = *si++;
                        goto case 8;
                    case 8:
                        *di++ = *si++;
                        goto case 7;
                    case 7:
                        *di++ = *si++;
                        goto case 6;
                    case 6:
                        *di++ = *si++;
                        goto case 5;
                    case 5:
                        *di++ = *si++;
                        goto case 4;
                    case 4:
                        *di++ = *si++;
                        goto case 3;
                    case 3:
                        *di++ = *si++;
                        goto case 2;
                    case 2:
                        *di++ = *si++;
                        goto case 1;
                    case 1:
                        *di++ = *si++;
                        if ((rem -= 16) > 0)
                            goto case 0;
                        break;
                }
                dest = di;
                source = si;
            }
            if (length != 0)
            {
                var db = (byte*)dest;
                var sb = (byte*)source;
                switch (length)
                {
                    case 3:
                        *db++ = *sb++;
                        goto case 2;
                    case 2:
                        *db++ = *sb++;
                        goto case 1;
                    case 1:
                        *db++ = *sb++;
                        break;
                }
            }
        }
        [SecurityCritical]
        [ExcludeFromCodeCoverage]
        private static unsafe void MemCpy2ByteAligned(void* dest, void* source, long length)
        {
            if (length >= sizeof(short))
            {
                var dl = (short*)dest;
                var sl = (short*)source;
                long rem = length >> 1;
                length -= rem << 1;
                switch (rem & 15)
                {
                    case 0:
                        *dl++ = *sl++;
                        goto case 15;
                    case 15:
                        *dl++ = *sl++;
                        goto case 14;
                    case 14:
                        *dl++ = *sl++;
                        goto case 13;
                    case 13:
                        *dl++ = *sl++;
                        goto case 12;
                    case 12:
                        *dl++ = *sl++;
                        goto case 11;
                    case 11:
                        *dl++ = *sl++;
                        goto case 10;
                    case 10:
                        *dl++ = *sl++;
                        goto case 9;
                    case 9:
                        *dl++ = *sl++;
                        goto case 8;
                    case 8:
                        *dl++ = *sl++;
                        goto case 7;
                    case 7:
                        *dl++ = *sl++;
                        goto case 6;
                    case 6:
                        *dl++ = *sl++;
                        goto case 5;
                    case 5:
                        *dl++ = *sl++;
                        goto case 4;
                    case 4:
                        *dl++ = *sl++;
                        goto case 3;
                    case 3:
                        *dl++ = *sl++;
                        goto case 2;
                    case 2:
                        *dl++ = *sl++;
                        goto case 1;
                    case 1:
                        *dl++ = *sl++;
                        if ((rem -= 16) > 0)
                            goto case 0;
                        break;
                }
                dest = dl;
                source = sl;
            }
            if (length != 0)
                *(byte*)dest = *(byte*)source;
        }
        [SecurityCritical]
        [ExcludeFromCodeCoverage]
        private static unsafe void MemCpyUnaligned(void* dest, void* source, long length)
        {
            var db = (byte*)dest;
            var sb = (byte*)source;
            switch (length & 15)
            {
                case 0:
                    *db++ = *sb++;
                    goto case 15;
                case 15:
                    *db++ = *sb++;
                    goto case 14;
                case 14:
                    *db++ = *sb++;
                    goto case 13;
                case 13:
                    *db++ = *sb++;
                    goto case 12;
                case 12:
                    *db++ = *sb++;
                    goto case 11;
                case 11:
                    *db++ = *sb++;
                    goto case 10;
                case 10:
                    *db++ = *sb++;
                    goto case 9;
                case 9:
                    *db++ = *sb++;
                    goto case 8;
                case 8:
                    *db++ = *sb++;
                    goto case 7;
                case 7:
                    *db++ = *sb++;
                    goto case 6;
                case 6:
                    *db++ = *sb++;
                    goto case 5;
                case 5:
                    *db++ = *sb++;
                    goto case 4;
                case 4:
                    *db++ = *sb++;
                    goto case 3;
                case 3:
                    *db++ = *sb++;
                    goto case 2;
                case 2:
                    *db++ = *sb++;
                    goto case 1;
                case 1:
                    *db++ = *sb++;
                    if ((length -= 16) > 0)
                        goto case 0;
                    return;
            }
        }
        [SecurityCritical]
        [ExcludeFromCodeCoverage]
        private static unsafe void MemCpy(void* dest, void* source, long length)
        {
            if (!AllowUnalignedRead)
            {
                int alignTest = (int)dest | (int)source;
                if ((alignTest & 1) != 0)
                {
                    MemCpyUnaligned(dest, source, length);
                    return;
                }
                if ((alignTest & 3) != 0)
                {
                    MemCpy2ByteAligned(dest, source, length);
                    return;
                }
                if ((alignTest & 7) != 0)
                {
                    MemCpy4ByteAligned(dest, source, length);
                    return;
                }
            }
            if (length >= 8)
            {
                var dl = (long*)dest;
                var sl = (long*)source;
                long rem = length >> 3;
                length -= rem << 3;
                switch (rem & 15)
                {
                    case 0:
                        *dl++ = *sl++;
                        goto case 15;
                    case 15:
                        *dl++ = *sl++;
                        goto case 14;
                    case 14:
                        *dl++ = *sl++;
                        goto case 13;
                    case 13:
                        *dl++ = *sl++;
                        goto case 12;
                    case 12:
                        *dl++ = *sl++;
                        goto case 11;
                    case 11:
                        *dl++ = *sl++;
                        goto case 10;
                    case 10:
                        *dl++ = *sl++;
                        goto case 9;
                    case 9:
                        *dl++ = *sl++;
                        goto case 8;
                    case 8:
                        *dl++ = *sl++;
                        goto case 7;
                    case 7:
                        *dl++ = *sl++;
                        goto case 6;
                    case 6:
                        *dl++ = *sl++;
                        goto case 5;
                    case 5:
                        *dl++ = *sl++;
                        goto case 4;
                    case 4:
                        *dl++ = *sl++;
                        goto case 3;
                    case 3:
                        *dl++ = *sl++;
                        goto case 2;
                    case 2:
                        *dl++ = *sl++;
                        goto case 1;
                    case 1:
                        *dl++ = *sl++;
                        if ((rem -= 16) > 0)
                            goto case 0;
                        break;
                }
                dest = dl;
                source = sl;
            }
            if (length != 0)
            {
                var db = (byte*)dest;
                var sb = (byte*)source;
                switch (length)
                {
                    case 7:
                        *db++ = *sb++;
                        goto case 6;
                    case 6:
                        *db++ = *sb++;
                        goto case 5;
                    case 5:
                        *db++ = *sb++;
                        goto case 4;
                    case 4:
                        *db++ = *sb++;
                        goto case 3;
                    case 3:
                        *db++ = *sb++;
                        goto case 2;
                    case 2:
                        *db++ = *sb++;
                        goto case 1;
                    case 1:
                        *db++ = *sb++;
                        break;
                }
            }
        }
        [SecurityCritical]
        [ExcludeFromCodeCoverage]
        private static unsafe void MemZero4ByteAligned(void* dest, long length)
        {
            if (length >= 4)
            {
                var di = (int*)dest;
                long rem = length >> 2;
                length -= rem << 2;
                switch (rem & 15)
                {
                    case 0:
                        *di++ = 0;
                        goto case 15;
                    case 15:
                        *di++ = 0;
                        goto case 14;
                    case 14:
                        *di++ = 0;
                        goto case 13;
                    case 13:
                        *di++ = 0;
                        goto case 12;
                    case 12:
                        *di++ = 0;
                        goto case 11;
                    case 11:
                        *di++ = 0;
                        goto case 10;
                    case 10:
                        *di++ = 0;
                        goto case 9;
                    case 9:
                        *di++ = 0;
                        goto case 8;
                    case 8:
                        *di++ = 0;
                        goto case 7;
                    case 7:
                        *di++ = 0;
                        goto case 6;
                    case 6:
                        *di++ = 0;
                        goto case 5;
                    case 5:
                        *di++ = 0;
                        goto case 4;
                    case 4:
                        *di++ = 0;
                        goto case 3;
                    case 3:
                        *di++ = 0;
                        goto case 2;
                    case 2:
                        *di++ = 0;
                        goto case 1;
                    case 1:
                        *di++ = 0;
                        if ((rem -= 16) > 0)
                            goto case 0;
                        break;
                }
                dest = di;
            }
            if (length != 0)
            {
                var db = (byte*)dest;
                switch (length)
                {
                    case 3:
                        *db++ = 0;
                        goto case 2;
                    case 2:
                        *db++ = 0;
                        goto case 1;
                    case 1:
                        *db++ = 0;
                        break;
                }
            }
        }
        [SecurityCritical]
        [ExcludeFromCodeCoverage]
        private static unsafe void MemZero2ByteAligned(void* dest, long length)
        {
            if (length >= sizeof(short))
            {
                var dl = (short*)dest;
                long rem = length >> 1;
                length -= rem << 1;
                switch (rem & 15)
                {
                    case 0:
                        *dl++ = 0;
                        goto case 15;
                    case 15:
                        *dl++ = 0;
                        goto case 14;
                    case 14:
                        *dl++ = 0;
                        goto case 13;
                    case 13:
                        *dl++ = 0;
                        goto case 12;
                    case 12:
                        *dl++ = 0;
                        goto case 11;
                    case 11:
                        *dl++ = 0;
                        goto case 10;
                    case 10:
                        *dl++ = 0;
                        goto case 9;
                    case 9:
                        *dl++ = 0;
                        goto case 8;
                    case 8:
                        *dl++ = 0;
                        goto case 7;
                    case 7:
                        *dl++ = 0;
                        goto case 6;
                    case 6:
                        *dl++ = 0;
                        goto case 5;
                    case 5:
                        *dl++ = 0;
                        goto case 4;
                    case 4:
                        *dl++ = 0;
                        goto case 3;
                    case 3:
                        *dl++ = 0;
                        goto case 2;
                    case 2:
                        *dl++ = 0;
                        goto case 1;
                    case 1:
                        *dl++ = 0;
                        if ((rem -= 16) > 0)
                            goto case 0;
                        break;
                }
                dest = dl;
            }
            if (length != 0)
                *(byte*)dest = 0;
        }
        [SecurityCritical]
        [ExcludeFromCodeCoverage]
        private static unsafe void MemZeroUnaligned(void* dest, long length)
        {
            var db = (byte*)dest;
            switch (length & 15)
            {
                case 0:
                    *db++ = 0;
                    goto case 15;
                case 15:
                    *db++ = 0;
                    goto case 14;
                case 14:
                    *db++ = 0;
                    goto case 13;
                case 13:
                    *db++ = 0;
                    goto case 12;
                case 12:
                    *db++ = 0;
                    goto case 11;
                case 11:
                    *db++ = 0;
                    goto case 10;
                case 10:
                    *db++ = 0;
                    goto case 9;
                case 9:
                    *db++ = 0;
                    goto case 8;
                case 8:
                    *db++ = 0;
                    goto case 7;
                case 7:
                    *db++ = 0;
                    goto case 6;
                case 6:
                    *db++ = 0;
                    goto case 5;
                case 5:
                    *db++ = 0;
                    goto case 4;
                case 4:
                    *db++ = 0;
                    goto case 3;
                case 3:
                    *db++ = 0;
                    goto case 2;
                case 2:
                    *db++ = 0;
                    goto case 1;
                case 1:
                    *db++ = 0;
                    if ((length -= 16) > 0)
                        goto case 0;
                    return;
            }
        }
        [SecurityCritical]
        [ExcludeFromCodeCoverage]
        private static unsafe void MemZero(void* dest, long length)
        {
            if (!AllowUnalignedRead)
            {
                int alignTest = (int)dest;
                if ((alignTest & 1) != 0)
                {
                    MemZeroUnaligned(dest, length);
                    return;
                }
                if ((alignTest & 3) != 0)
                {
                    MemZero2ByteAligned(dest, length);
                    return;
                }
                if ((alignTest & 7) != 0)
                {
                    MemZero4ByteAligned(dest, length);
                    return;
                }
            }
            if (length >= 8)
            {
                var dl = (long*)dest;
                long rem = length >> 3;
                length -= rem << 3;
                switch (rem & 15)
                {
                    case 0:
                        *dl++ = 0;
                        goto case 15;
                    case 15:
                        *dl++ = 0;
                        goto case 14;
                    case 14:
                        *dl++ = 0;
                        goto case 13;
                    case 13:
                        *dl++ = 0;
                        goto case 12;
                    case 12:
                        *dl++ = 0;
                        goto case 11;
                    case 11:
                        *dl++ = 0;
                        goto case 10;
                    case 10:
                        *dl++ = 0;
                        goto case 9;
                    case 9:
                        *dl++ = 0;
                        goto case 8;
                    case 8:
                        *dl++ = 0;
                        goto case 7;
                    case 7:
                        *dl++ = 0;
                        goto case 6;
                    case 6:
                        *dl++ = 0;
                        goto case 5;
                    case 5:
                        *dl++ = 0;
                        goto case 4;
                    case 4:
                        *dl++ = 0;
                        goto case 3;
                    case 3:
                        *dl++ = 0;
                        goto case 2;
                    case 2:
                        *dl++ = 0;
                        goto case 1;
                    case 1:
                        *dl++ = 0;
                        if ((rem -= 16) > 0)
                            goto case 0;
                        break;
                }
                dest = dl;
            }
            if (length != 0)
            {
                var db = (byte*)dest;
                switch (length)
                {
                    case 7:
                        *db++ = 0;
                        goto case 6;
                    case 6:
                        *db++ = 0;
                        goto case 5;
                    case 5:
                        *db++ = 0;
                        goto case 4;
                    case 4:
                        *db++ = 0;
                        goto case 3;
                    case 3:
                        *db++ = 0;
                        goto case 2;
                    case 2:
                        *db++ = 0;
                        goto case 1;
                    case 1:
                        *db++ = 0;
                        break;
                }
            }
        }


    }
}
