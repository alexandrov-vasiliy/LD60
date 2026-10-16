using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TextureLab.Runtime
{
    public sealed class TextureLabZipLimits
    {
        public static TextureLabZipLimits Default { get; } = new TextureLabZipLimits(
            maxEntries: 16,
            maxEntryBytes: 64 * 1024 * 1024,
            maxTotalBytes: 96 * 1024 * 1024,
            maxPathLength: 240);

        public TextureLabZipLimits(int maxEntries, int maxEntryBytes, int maxTotalBytes, int maxPathLength)
        {
            MaxEntries = maxEntries;
            MaxEntryBytes = maxEntryBytes;
            MaxTotalBytes = maxTotalBytes;
            MaxPathLength = maxPathLength;
        }

        public int MaxEntries { get; }
        public int MaxEntryBytes { get; }
        public int MaxTotalBytes { get; }
        public int MaxPathLength { get; }
    }

    public sealed class TextureLabZipEntry
    {
        public TextureLabZipEntry(string name, byte[] data)
        {
            Name = name;
            Data = data;
        }

        public string Name { get; }
        public byte[] Data { get; }
    }

    /// <summary>
    /// Small ZIP store-only reader/writer used by the portable document spike.
    /// Keeping compression out of this layer avoids a System.IO.Compression dependency
    /// that is not guaranteed to be present in every IL2CPP/Web profile.
    /// </summary>
    public static class TextureLabZipArchive
    {
        private const uint LocalFileHeader = 0x04034b50;
        private const uint CentralDirectoryHeader = 0x02014b50;
        private const uint EndOfCentralDirectory = 0x06054b50;
        private const int LocalHeaderSize = 30;
        private const int CentralHeaderSize = 46;
        private const int EndOfCentralDirectorySize = 22;
        private const ushort Utf8Flag = 0x0800;

        public static byte[] Create(IReadOnlyList<TextureLabZipEntry> entries)
        {
            return Create(entries, TextureLabZipLimits.Default);
        }

        public static byte[] Create(IReadOnlyList<TextureLabZipEntry> entries, TextureLabZipLimits limits)
        {
            TextureLabValidationResult validation = ValidateEntries(entries, limits);
            if (!validation.IsValid)
                throw new TextureLabSerializationException($"Invalid ZIP entries: {validation}");

            var offsets = new int[entries.Count];
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                for (int index = 0; index < entries.Count; index++)
                {
                    TextureLabZipEntry entry = entries[index];
                    byte[] name = EncodeName(entry.Name);
                    byte[] data = entry.Data ?? Array.Empty<byte>();
                    offsets[index] = checked((int)stream.Position);
                    uint crc = TextureLabCrc32.Compute(data);

                    writer.Write(LocalFileHeader);
                    writer.Write((ushort)20);
                    writer.Write(Utf8Flag);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write(crc);
                    writer.Write((uint)data.Length);
                    writer.Write((uint)data.Length);
                    writer.Write((ushort)name.Length);
                    writer.Write((ushort)0);
                    writer.Write(name);
                    writer.Write(data);
                }

                int centralOffset = checked((int)stream.Position);
                for (int index = 0; index < entries.Count; index++)
                {
                    TextureLabZipEntry entry = entries[index];
                    byte[] name = EncodeName(entry.Name);
                    byte[] data = entry.Data ?? Array.Empty<byte>();
                    uint crc = TextureLabCrc32.Compute(data);

                    writer.Write(CentralDirectoryHeader);
                    writer.Write((ushort)20);
                    writer.Write((ushort)20);
                    writer.Write(Utf8Flag);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write(crc);
                    writer.Write((uint)data.Length);
                    writer.Write((uint)data.Length);
                    writer.Write((ushort)name.Length);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write((uint)0);
                    writer.Write((uint)offsets[index]);
                    writer.Write(name);
                }

                int centralSize = checked((int)stream.Position - centralOffset);
                writer.Write(EndOfCentralDirectory);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((ushort)entries.Count);
                writer.Write((ushort)entries.Count);
                writer.Write((uint)centralSize);
                writer.Write((uint)centralOffset);
                writer.Write((ushort)0);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static bool TryRead(
            byte[] archive,
            out List<TextureLabZipEntry> entries,
            out TextureLabValidationResult validation)
        {
            return TryRead(archive, TextureLabZipLimits.Default, out entries, out validation);
        }

        public static bool TryRead(
            byte[] archive,
            TextureLabZipLimits limits,
            out List<TextureLabZipEntry> entries,
            out TextureLabValidationResult validation)
        {
            try
            {
                return TryReadCore(archive, limits, out entries, out validation);
            }
            catch (Exception exception)
            {
                entries = new List<TextureLabZipEntry>();
                validation = new TextureLabValidationResult();
                validation.Add("container", $"The ZIP container is invalid: {exception.Message}");
                return false;
            }
        }

        private static bool TryReadCore(
            byte[] archive,
            TextureLabZipLimits limits,
            out List<TextureLabZipEntry> entries,
            out TextureLabValidationResult validation)
        {
            entries = new List<TextureLabZipEntry>();
            validation = new TextureLabValidationResult();
            if (archive == null || archive.Length < EndOfCentralDirectorySize)
            {
                validation.Add("container", "The ZIP container is missing or truncated.");
                return false;
            }

            if (!TryFindEndRecord(archive, out int endOffset))
            {
                validation.Add("container", "The ZIP end record is missing or invalid.");
                return false;
            }

            ushort entryCount = ReadUInt16(archive, endOffset + 10);
            uint centralSizeValue = ReadUInt32(archive, endOffset + 12);
            uint centralOffsetValue = ReadUInt32(archive, endOffset + 16);
            if (entryCount == 0 || entryCount > limits.MaxEntries)
                validation.Add("container", "The ZIP contains too many entries.");
            if (centralSizeValue > int.MaxValue || centralOffsetValue > int.MaxValue)
                validation.Add("container", "ZIP64 containers are not supported.");
            if (!validation.IsValid)
                return false;

            int centralSize = (int)centralSizeValue;
            int centralOffset = (int)centralOffsetValue;
            if (centralOffset < 0 || centralSize < 0 || centralOffset > archive.Length - centralSize)
            {
                validation.Add("container", "The ZIP central directory is outside the archive.");
                return false;
            }

            int cursor = centralOffset;
            long totalBytes = 0;
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < entryCount; index++)
            {
                if (cursor > centralOffset + centralSize - CentralHeaderSize
                    || ReadUInt32(archive, cursor) != CentralDirectoryHeader)
                {
                    validation.Add($"entries[{index}]", "The central directory entry is invalid.");
                    return false;
                }

                ushort flags = ReadUInt16(archive, cursor + 8);
                ushort method = ReadUInt16(archive, cursor + 10);
                uint crc = ReadUInt32(archive, cursor + 16);
                uint compressedSizeValue = ReadUInt32(archive, cursor + 20);
                uint uncompressedSizeValue = ReadUInt32(archive, cursor + 24);
                ushort nameLength = ReadUInt16(archive, cursor + 28);
                ushort extraLength = ReadUInt16(archive, cursor + 30);
                ushort commentLength = ReadUInt16(archive, cursor + 32);
                uint localOffsetValue = ReadUInt32(archive, cursor + 42);
                int headerEnd = checked(cursor + CentralHeaderSize + nameLength + extraLength + commentLength);
                if (headerEnd > centralOffset + centralSize)
                {
                    validation.Add($"entries[{index}]", "The central directory entry is truncated.");
                    return false;
                }

                if ((flags & 0x0009) != 0 || method != 0)
                {
                    validation.Add($"entries[{index}]", "Only unencrypted, store-only ZIP entries are supported.");
                    return false;
                }
                if (compressedSizeValue != uncompressedSizeValue
                    || compressedSizeValue > int.MaxValue
                    || compressedSizeValue > limits.MaxEntryBytes)
                {
                    validation.Add($"entries[{index}]", "ZIP entry size or compression is unsupported.");
                    return false;
                }

                string name;
                try
                {
                    name = DecodeName(archive, cursor + CentralHeaderSize, nameLength);
                }
                catch (Exception exception)
                {
                    validation.Add($"entries[{index}]", $"Entry name is invalid: {exception.Message}");
                    return false;
                }

                if (!IsSafePath(name, limits.MaxPathLength) || !names.Add(name))
                {
                    validation.Add($"entries[{index}]", "Entry path is unsafe or duplicated.");
                    return false;
                }

                if (localOffsetValue > int.MaxValue)
                {
                    validation.Add($"entries[{index}]", "ZIP64 local offsets are not supported.");
                    return false;
                }

                int localOffset = (int)localOffsetValue;
                if (localOffset < 0 || localOffset > archive.Length - LocalHeaderSize
                    || ReadUInt32(archive, localOffset) != LocalFileHeader)
                {
                    validation.Add($"entries[{index}]", "The local file header is invalid.");
                    return false;
                }

                ushort localFlags = ReadUInt16(archive, localOffset + 6);
                ushort localMethod = ReadUInt16(archive, localOffset + 8);
                ushort localNameLength = ReadUInt16(archive, localOffset + 26);
                ushort localExtraLength = ReadUInt16(archive, localOffset + 28);
                if (localFlags != flags || localMethod != method || localNameLength != nameLength)
                {
                    validation.Add($"entries[{index}]", "Local and central ZIP headers disagree.");
                    return false;
                }

                string localName = DecodeName(archive, localOffset + LocalHeaderSize, localNameLength);
                if (!string.Equals(localName, name, StringComparison.Ordinal))
                {
                    validation.Add($"entries[{index}]", "Local and central entry names disagree.");
                    return false;
                }

                int dataOffset = checked(localOffset + LocalHeaderSize + localNameLength + localExtraLength);
                int dataLength = (int)compressedSizeValue;
                if (dataOffset < 0 || dataLength < 0 || dataOffset > archive.Length - dataLength)
                {
                    validation.Add($"entries[{index}]", "Entry data is outside the archive.");
                    return false;
                }

                byte[] data = new byte[dataLength];
                Buffer.BlockCopy(archive, dataOffset, data, 0, dataLength);
                if (TextureLabCrc32.Compute(data) != crc)
                {
                    validation.Add($"entries[{index}]", "Entry CRC does not match.");
                    return false;
                }

                totalBytes += dataLength;
                if (totalBytes > limits.MaxTotalBytes)
                {
                    validation.Add("container", "ZIP entries exceed the total size limit.");
                    return false;
                }

                entries.Add(new TextureLabZipEntry(name, data));
                cursor = headerEnd;
            }

            if (cursor != centralOffset + centralSize)
            {
                validation.Add("container", "The ZIP central directory has trailing data.");
                entries.Clear();
                return false;
            }

            return true;
        }

        private static TextureLabValidationResult ValidateEntries(IReadOnlyList<TextureLabZipEntry> entries, TextureLabZipLimits limits)
        {
            var validation = new TextureLabValidationResult();
            if (limits == null || limits.MaxEntries < 1 || limits.MaxEntryBytes < 1 || limits.MaxTotalBytes < 1)
            {
                validation.Add("limits", "ZIP limits are invalid.");
                return validation;
            }
            if (entries == null)
            {
                validation.Add("entries", "ZIP entries are missing.");
                return validation;
            }
            if (entries.Count == 0 || entries.Count > limits.MaxEntries || entries.Count > ushort.MaxValue)
                validation.Add("entries", "Entry count is outside the supported range.");

            var names = new HashSet<string>(StringComparer.Ordinal);
            long totalBytes = 0;
            for (int index = 0; index < entries.Count; index++)
            {
                TextureLabZipEntry entry = entries[index];
                if (entry == null)
                {
                    validation.Add($"entries[{index}]", "Entry is missing.");
                    continue;
                }
                if (!IsSafePath(entry.Name, limits.MaxPathLength) || !names.Add(entry.Name))
                    validation.Add($"entries[{index}].name", "Entry path is unsafe or duplicated.");
                if (entry.Data == null || entry.Data.Length > limits.MaxEntryBytes)
                    validation.Add($"entries[{index}].data", "Entry data is missing or too large.");
                else
                    totalBytes += entry.Data.Length;
            }

            if (totalBytes > limits.MaxTotalBytes)
                validation.Add("entries", "Entry data exceeds the total size limit.");
            return validation;
        }

        private static bool TryFindEndRecord(byte[] archive, out int offset)
        {
            int minimum = Math.Max(0, archive.Length - (EndOfCentralDirectorySize + ushort.MaxValue));
            for (int index = archive.Length - EndOfCentralDirectorySize; index >= minimum; index--)
            {
                if (ReadUInt32(archive, index) != EndOfCentralDirectory)
                    continue;

                ushort commentLength = ReadUInt16(archive, index + 20);
                if (index + EndOfCentralDirectorySize + commentLength == archive.Length)
                {
                    offset = index;
                    return true;
                }
            }

            offset = 0;
            return false;
        }

        private static bool IsSafePath(string path, int maxLength)
        {
            if (string.IsNullOrEmpty(path) || path.Length > maxLength || path[0] == '/' || path[0] == '\\')
                return false;
            if (path.IndexOf(':') >= 0 || path.IndexOf('\\') >= 0)
                return false;

            string[] segments = path.Split('/');
            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                    return false;
                for (int index = 0; index < segment.Length; index++)
                {
                    if (char.IsControl(segment[index]))
                        return false;
                }
            }
            return true;
        }

        private static byte[] EncodeName(string name) => new UTF8Encoding(false, true).GetBytes(name);

        private static string DecodeName(byte[] data, int offset, int length)
        {
            return new UTF8Encoding(false, true).GetString(data, offset, length);
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24));
        }
    }

    internal static class TextureLabCrc32
    {
        private static readonly uint[] Table = BuildTable();

        internal static uint Compute(byte[] data)
        {
            uint crc = 0xffffffffu;
            if (data != null)
            {
                for (int index = 0; index < data.Length; index++)
                    crc = Table[(crc ^ data[index]) & 0xff] ^ (crc >> 8);
            }
            return crc ^ 0xffffffffu;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint index = 0; index < table.Length; index++)
            {
                uint value = index;
                for (int bit = 0; bit < 8; bit++)
                    value = (value & 1) != 0 ? 0xedb88320u ^ (value >> 1) : value >> 1;
                table[(int)index] = value;
            }
            return table;
        }
    }
}
