// SPDX-License-Identifier: GPL-3.0-or-later
// NetForge Studio - codificador QR propio (modo byte, versiones 1 a 10, niveles L/M/Q/H).
//
// La app necesita generar un QR sin internet y sin DLLs de terceros, asi que se implementa
// el algoritmo completo: Reed-Solomon sobre GF(256), eleccion de mascara por penalizacion y
// colocacion de modulos segun ISO/IEC 18004.
//
// Verificacion (build/check-qr): los codigos generados se decodifican con una biblioteca
// independiente (ZXing) que NO forma parte del producto, y ademas se comprueba la
// informacion de formato contra la tabla publicada del estandar.
using System;
using System.Collections.Generic;
using System.Text;

namespace NetForge.Qr
{
    public enum QrEcc
    {
        /// <summary>Recupera ~7% de los datos.</summary>
        Low = 0,

        /// <summary>Recupera ~15% (recomendado para pantalla).</summary>
        Medium = 1,

        /// <summary>Recupera ~25%.</summary>
        Quartile = 2,

        /// <summary>Recupera ~30%.</summary>
        High = 3
    }

    public sealed class QrCode
    {
        public QrCode(bool[,] modules, int version, QrEcc ecc, int mask)
        {
            Modules = modules;
            Version = version;
            Ecc = ecc;
            Mask = mask;
            Size = modules.GetLength(0);
        }

        /// <summary>Modulos en orden [fila, columna]; true es oscuro.</summary>
        public bool[,] Modules { get; private set; }

        public int Size { get; private set; }

        public int Version { get; private set; }

        public QrEcc Ecc { get; private set; }

        public int Mask { get; private set; }

        public bool this[int row, int column]
        {
            get { return Modules[row, column]; }
        }
    }

    public static class QrEncoder
    {
        private const int MinVersion = 1;
        private const int MaxVersion = 10;

        private struct BlockSpec
        {
            public int EccPerBlock;
            public int Group1Blocks;
            public int Group1Data;
            public int Group2Blocks;
            public int Group2Data;

            public BlockSpec(int ecc, int g1Blocks, int g1Data, int g2Blocks, int g2Data)
            {
                EccPerBlock = ecc;
                Group1Blocks = g1Blocks;
                Group1Data = g1Data;
                Group2Blocks = g2Blocks;
                Group2Data = g2Data;
            }

            public int DataCodewords
            {
                get { return Group1Blocks * Group1Data + Group2Blocks * Group2Data; }
            }

            public int TotalCodewords
            {
                get { return DataCodewords + (Group1Blocks + Group2Blocks) * EccPerBlock; }
            }
        }

        // Tablas del estandar para versiones 1..10, en orden L, M, Q, H.
        private static readonly BlockSpec[][] Specs = new BlockSpec[][]
        {
            new BlockSpec[] { new BlockSpec(7, 1, 19, 0, 0),   new BlockSpec(10, 1, 16, 0, 0),  new BlockSpec(13, 1, 13, 0, 0),  new BlockSpec(17, 1, 9, 0, 0) },
            new BlockSpec[] { new BlockSpec(10, 1, 34, 0, 0),  new BlockSpec(16, 1, 28, 0, 0),  new BlockSpec(22, 1, 22, 0, 0),  new BlockSpec(28, 1, 16, 0, 0) },
            new BlockSpec[] { new BlockSpec(15, 1, 55, 0, 0),  new BlockSpec(26, 1, 44, 0, 0),  new BlockSpec(18, 2, 17, 0, 0),  new BlockSpec(22, 2, 13, 0, 0) },
            new BlockSpec[] { new BlockSpec(20, 1, 80, 0, 0),  new BlockSpec(18, 2, 32, 0, 0),  new BlockSpec(26, 2, 24, 0, 0),  new BlockSpec(16, 4, 9, 0, 0) },
            new BlockSpec[] { new BlockSpec(26, 1, 108, 0, 0), new BlockSpec(24, 2, 43, 0, 0),  new BlockSpec(18, 2, 15, 2, 16), new BlockSpec(22, 2, 11, 2, 12) },
            new BlockSpec[] { new BlockSpec(18, 2, 68, 0, 0),  new BlockSpec(16, 4, 27, 0, 0),  new BlockSpec(24, 4, 19, 0, 0),  new BlockSpec(28, 4, 15, 0, 0) },
            new BlockSpec[] { new BlockSpec(20, 2, 78, 0, 0),  new BlockSpec(18, 4, 31, 0, 0),  new BlockSpec(18, 2, 14, 4, 15), new BlockSpec(26, 4, 13, 1, 14) },
            new BlockSpec[] { new BlockSpec(24, 2, 97, 0, 0),  new BlockSpec(22, 2, 38, 2, 39), new BlockSpec(22, 4, 18, 2, 19), new BlockSpec(26, 4, 14, 2, 15) },
            new BlockSpec[] { new BlockSpec(30, 2, 116, 0, 0), new BlockSpec(22, 3, 36, 2, 37), new BlockSpec(20, 4, 16, 4, 17), new BlockSpec(24, 4, 12, 4, 13) },
            new BlockSpec[] { new BlockSpec(18, 2, 68, 2, 69), new BlockSpec(26, 4, 43, 1, 44), new BlockSpec(24, 6, 19, 2, 20), new BlockSpec(28, 6, 15, 2, 16) }
        };

        private static readonly int[][] AlignmentPositions = new int[][]
        {
            new int[] { },
            new int[] { 6, 18 },
            new int[] { 6, 22 },
            new int[] { 6, 26 },
            new int[] { 6, 30 },
            new int[] { 6, 34 },
            new int[] { 6, 22, 38 },
            new int[] { 6, 24, 42 },
            new int[] { 6, 26, 46 },
            new int[] { 6, 28, 50 }
        };

        public static QrCode Encode(string text, QrEcc ecc = QrEcc.Medium)
        {
            if (text == null)
            {
                throw new ArgumentNullException("text");
            }

            byte[] payload = Encoding.UTF8.GetBytes(text);
            int version = ChooseVersion(payload.Length, ecc);
            byte[] codewords = BuildCodewords(payload, version, ecc);
            return BuildMatrix(codewords, version, ecc);
        }

        /// <summary>Bytes utiles en modo byte para esta version y nivel.</summary>
        public static int ByteCapacity(int version, QrEcc ecc)
        {
            BlockSpec spec = SpecFor(version, ecc);
            int bits = spec.DataCodewords * 8 - 4 - CharacterCountBits(version);
            return bits / 8;
        }

        private static BlockSpec SpecFor(int version, QrEcc ecc)
        {
            if (version < MinVersion || version > MaxVersion)
            {
                throw new ArgumentOutOfRangeException("version", "Solo se admiten versiones " + MinVersion + " a " + MaxVersion + ".");
            }

            return Specs[version - 1][(int)ecc];
        }

        private static int CharacterCountBits(int version)
        {
            return version <= 9 ? 8 : 16;
        }

        private static int ChooseVersion(int byteCount, QrEcc ecc)
        {
            for (int version = MinVersion; version <= MaxVersion; version++)
            {
                if (ByteCapacity(version, ecc) >= byteCount)
                {
                    return version;
                }
            }

            throw new ArgumentException("El texto no cabe en un QR de version " + MaxVersion + " con el nivel " + ecc +
                                        " (" + byteCount + " bytes).");
        }

        private static byte[] BuildCodewords(byte[] payload, int version, QrEcc ecc)
        {
            BlockSpec spec = SpecFor(version, ecc);
            List<bool> bits = new List<bool>();
            AppendBits(0x4, 4, bits);                        // modo byte
            AppendBits(payload.Length, CharacterCountBits(version), bits);
            foreach (byte b in payload)
            {
                AppendBits(b, 8, bits);
            }

            int capacityBits = spec.DataCodewords * 8;
            int terminator = Math.Min(4, capacityBits - bits.Count);
            for (int i = 0; i < terminator; i++)
            {
                bits.Add(false);
            }

            while (bits.Count % 8 != 0)
            {
                bits.Add(false);
            }

            List<byte> data = new List<byte>(spec.DataCodewords);
            for (int i = 0; i < bits.Count; i += 8)
            {
                int value = 0;
                for (int j = 0; j < 8; j++)
                {
                    value = (value << 1) | (bits[i + j] ? 1 : 0);
                }

                data.Add((byte)value);
            }

            byte[] pad = new byte[] { 0xEC, 0x11 };
            int padIndex = 0;
            while (data.Count < spec.DataCodewords)
            {
                data.Add(pad[padIndex % 2]);
                padIndex++;
            }

            return Interleave(data.ToArray(), spec);
        }

        private static byte[] Interleave(byte[] data, BlockSpec spec)
        {
            int blockCount = spec.Group1Blocks + spec.Group2Blocks;
            int maxData = Math.Max(spec.Group1Data, spec.Group2Data);
            byte[][] dataBlocks = new byte[blockCount][];
            byte[][] eccBlocks = new byte[blockCount][];
            byte[] generator = ReedSolomon.Generator(spec.EccPerBlock);

            int offset = 0;
            for (int i = 0; i < blockCount; i++)
            {
                int size = i < spec.Group1Blocks ? spec.Group1Data : spec.Group2Data;
                byte[] block = new byte[size];
                Array.Copy(data, offset, block, 0, size);
                offset += size;
                dataBlocks[i] = block;
                eccBlocks[i] = ReedSolomon.Remainder(block, generator);
            }

            List<byte> result = new List<byte>(spec.TotalCodewords);
            for (int i = 0; i < maxData; i++)
            {
                for (int b = 0; b < blockCount; b++)
                {
                    if (i < dataBlocks[b].Length)
                    {
                        result.Add(dataBlocks[b][i]);
                    }
                }
            }

            for (int i = 0; i < spec.EccPerBlock; i++)
            {
                for (int b = 0; b < blockCount; b++)
                {
                    result.Add(eccBlocks[b][i]);
                }
            }

            return result.ToArray();
        }

        private static QrCode BuildMatrix(byte[] codewords, int version, QrEcc ecc)
        {
            int size = version * 4 + 17;
            bool[][] isFunction = new bool[size][];
            for (int y = 0; y < size; y++)
            {
                isFunction[y] = new bool[size];
            }

            bool[][] best = null;
            int bestMask = 0;
            int bestPenalty = int.MaxValue;

            for (int mask = 0; mask < 8; mask++)
            {
                bool[][] modules = NewMatrix(size);
                bool[][] function = NewMatrix(size);
                DrawFunctionPatterns(modules, function, version, size);

                int bitIndex = 0;
                for (int right = size - 1; right >= 1; right -= 2)
                {
                    if (right == 6)
                    {
                        right = 5;
                    }

                    for (int vert = 0; vert < size; vert++)
                    {
                        for (int j = 0; j < 2; j++)
                        {
                            int x = right - j;
                            bool upward = ((right + 1) & 2) == 0;
                            int y = upward ? size - 1 - vert : vert;
                            if (function[y][x] || bitIndex >= codewords.Length * 8)
                            {
                                continue;
                            }

                            bool bit = ((codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                            if (MaskCondition(mask, x, y))
                            {
                                bit = !bit;
                            }

                            modules[y][x] = bit;
                            bitIndex++;
                        }
                    }
                }

                DrawFormatBits(modules, function, ecc, mask, size);

                int penalty = Penalty(modules, size);
                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    bestMask = mask;
                    best = modules;
                }
            }

            return new QrCode(Convert(best), version, ecc, bestMask);
        }

        private static bool[,] Convert(bool[][] source)
        {
            int size = source.Length;
            bool[,] result = new bool[size, size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    result[y, x] = source[y][x];
                }
            }

            return result;
        }

        private static bool[][] NewMatrix(int size)
        {
            bool[][] matrix = new bool[size][];
            for (int y = 0; y < size; y++)
            {
                matrix[y] = new bool[size];
            }

            return matrix;
        }

        private static void DrawFunctionPatterns(bool[][] modules, bool[][] function, int version, int size)
        {
            // Patrones de posicion (finder) y sus separadores.
            for (int i = 0; i < size; i++)
            {
                SetFunction(modules, function, 6, i, i % 2 == 0);
                SetFunction(modules, function, i, 6, i % 2 == 0);
            }

            DrawFinder(modules, function, 3, 3, size);
            DrawFinder(modules, function, size - 4, 3, size);
            DrawFinder(modules, function, 3, size - 4, size);

            // Patrones de alineacion.
            int[] positions = AlignmentPositions[version - 1];
            int count = positions.Length;
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    if ((i == 0 && j == 0) || (i == 0 && j == count - 1) || (i == count - 1 && j == 0))
                    {
                        continue;
                    }

                    DrawAlignment(modules, function, positions[i], positions[j]);
                }
            }

            // Informacion de formato (se reescribe al elegir mascara) y hueco de version.
            DrawFormatBits(modules, function, QrEcc.Medium, 0, size);
            DrawVersionBits(modules, function, version, size);
        }

        private static void DrawFinder(bool[][] modules, bool[][] function, int centerX, int centerY, int size)
        {
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = centerX + dx;
                    int y = centerY + dy;
                    if (x < 0 || x >= size || y < 0 || y >= size)
                    {
                        continue;
                    }

                    int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    SetFunction(modules, function, x, y, distance != 2 && distance != 4);
                }
            }
        }

        private static void DrawAlignment(bool[][] modules, bool[][] function, int centerX, int centerY)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    SetFunction(modules, function, centerX + dx, centerY + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            }
        }

        private static void DrawFormatBits(bool[][] modules, bool[][] function, QrEcc ecc, int mask, int size)
        {
            int data = (EccBits(ecc) << 3) | mask;
            int remainder = data;
            for (int i = 0; i < 10; i++)
            {
                remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
            }

            int bits = ((data << 10) | remainder) ^ 0x5412;

            for (int i = 0; i <= 5; i++)
            {
                SetFunction(modules, function, 8, i, GetBit(bits, i));
            }

            SetFunction(modules, function, 8, 7, GetBit(bits, 6));
            SetFunction(modules, function, 8, 8, GetBit(bits, 7));
            SetFunction(modules, function, 7, 8, GetBit(bits, 8));
            for (int i = 9; i < 15; i++)
            {
                SetFunction(modules, function, 14 - i, 8, GetBit(bits, i));
            }

            for (int i = 0; i < 8; i++)
            {
                SetFunction(modules, function, size - 1 - i, 8, GetBit(bits, i));
            }

            for (int i = 8; i < 15; i++)
            {
                SetFunction(modules, function, 8, size - 15 + i, GetBit(bits, i));
            }

            SetFunction(modules, function, 8, size - 8, true);
        }

        private static void DrawVersionBits(bool[][] modules, bool[][] function, int version, int size)
        {
            if (version < 7)
            {
                return;
            }

            int remainder = version;
            for (int i = 0; i < 12; i++)
            {
                remainder = (remainder << 1) ^ ((remainder >> 11) * 0x1F25);
            }

            int bits = (version << 12) | remainder;
            for (int i = 0; i < 18; i++)
            {
                bool bit = GetBit(bits, i);
                int a = size - 11 + i % 3;
                int b = i / 3;
                SetFunction(modules, function, a, b, bit);
                SetFunction(modules, function, b, a, bit);
            }
        }

        private static void SetFunction(bool[][] modules, bool[][] function, int x, int y, bool dark)
        {
            if (x < 0 || y < 0 || y >= modules.Length || x >= modules.Length)
            {
                return;
            }

            modules[y][x] = dark;
            function[y][x] = true;
        }

        private static int EccBits(QrEcc ecc)
        {
            switch (ecc)
            {
                case QrEcc.Low:
                    return 1;
                case QrEcc.Medium:
                    return 0;
                case QrEcc.Quartile:
                    return 3;
                default:
                    return 2;
            }
        }

        private static bool GetBit(int value, int index)
        {
            return ((value >> index) & 1) != 0;
        }

        private static bool MaskCondition(int mask, int x, int y)
        {
            switch (mask)
            {
                case 0:
                    return (x + y) % 2 == 0;
                case 1:
                    return y % 2 == 0;
                case 2:
                    return x % 3 == 0;
                case 3:
                    return (x + y) % 3 == 0;
                case 4:
                    return (x / 3 + y / 2) % 2 == 0;
                case 5:
                    return x * y % 2 + x * y % 3 == 0;
                case 6:
                    return (x * y % 2 + x * y % 3) % 2 == 0;
                default:
                    return ((x + y) % 2 + x * y % 3) % 2 == 0;
            }
        }

        private static int Penalty(bool[][] modules, int size)
        {
            int score = 0;

            // Regla 1: secuencias largas del mismo color.
            for (int y = 0; y < size; y++)
            {
                int runLength = 1;
                for (int x = 1; x < size; x++)
                {
                    if (modules[y][x] == modules[y][x - 1])
                    {
                        runLength++;
                    }
                    else
                    {
                        if (runLength >= 5)
                        {
                            score += 3 + (runLength - 5);
                        }

                        runLength = 1;
                    }
                }

                if (runLength >= 5)
                {
                    score += 3 + (runLength - 5);
                }
            }

            for (int x = 0; x < size; x++)
            {
                int runLength = 1;
                for (int y = 1; y < size; y++)
                {
                    if (modules[y][x] == modules[y - 1][x])
                    {
                        runLength++;
                    }
                    else
                    {
                        if (runLength >= 5)
                        {
                            score += 3 + (runLength - 5);
                        }

                        runLength = 1;
                    }
                }

                if (runLength >= 5)
                {
                    score += 3 + (runLength - 5);
                }
            }

            // Regla 2: bloques 2x2 del mismo color.
            for (int y = 0; y < size - 1; y++)
            {
                for (int x = 0; x < size - 1; x++)
                {
                    bool value = modules[y][x];
                    if (value == modules[y][x + 1] && value == modules[y + 1][x] && value == modules[y + 1][x + 1])
                    {
                        score += 3;
                    }
                }
            }

            // Regla 3: patron tipo finder con cuatro modulos claros.
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (x + 6 < size && IsFinderLike(modules[y][x], modules[y][x + 1], modules[y][x + 2], modules[y][x + 3],
                            modules[y][x + 4], modules[y][x + 5], modules[y][x + 6]))
                    {
                        score += 40;
                    }

                    if (y + 6 < size && IsFinderLike(modules[y][x], modules[y + 1][x], modules[y + 2][x], modules[y + 3][x],
                            modules[y + 4][x], modules[y + 5][x], modules[y + 6][x]))
                    {
                        score += 40;
                    }
                }
            }

            // Regla 4: proporcion de modulos oscuros.
            int dark = 0;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (modules[y][x])
                    {
                        dark++;
                    }
                }
            }

            int total = size * size;
            int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            score += k * 10;

            return score;
        }

        private static bool IsFinderLike(bool a, bool b, bool c, bool d, bool e, bool f, bool g)
        {
            return a && !b && c && d && e && !f && g;
        }

        private static void AppendBits(int value, int length, List<bool> bits)
        {
            for (int i = length - 1; i >= 0; i--)
            {
                bits.Add(((value >> i) & 1) != 0);
            }
        }
    }

    /// <summary>Aritmetica de Reed-Solomon sobre GF(256) con polinomio primitivo 0x11D.</summary>
    internal static class ReedSolomon
    {
        private static readonly byte[] Log = new byte[256];
        private static readonly byte[] Exp = new byte[256];

        static ReedSolomon()
        {
            int value = 1;
            for (int i = 0; i < 255; i++)
            {
                Exp[i] = (byte)value;
                Log[value] = (byte)i;
                value <<= 1;
                if ((value & 0x100) != 0)
                {
                    value ^= 0x11D;
                }
            }

            Exp[255] = Exp[0];
        }

        public static byte Multiply(byte left, byte right)
        {
            if (left == 0 || right == 0)
            {
                return 0;
            }

            return Exp[(Log[left] + Log[right]) % 255];
        }

        public static byte[] Generator(int degree)
        {
            byte[] result = new byte[degree];
            result[degree - 1] = 1;
            byte root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = Multiply(result[j], root);
                    if (j + 1 < degree)
                    {
                        result[j] ^= result[j + 1];
                    }
                }

                root = Multiply(root, 0x02);
            }

            return result;
        }

        public static byte[] Remainder(byte[] data, byte[] generator)
        {
            byte[] result = new byte[generator.Length];
            foreach (byte value in data)
            {
                byte factor = (byte)(value ^ result[0]);
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] ^= Multiply(generator[i], factor);
                }
            }

            return result;
        }
    }
}
