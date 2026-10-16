using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TextureLab.Runtime
{
    /// <summary>
    /// Result of a local-data operation. Platform adapters can map their own
    /// storage errors to the same stable codes without exposing exceptions to
    /// the application layer.
    /// </summary>
    public readonly struct TextureLabStorageResult
    {
        private TextureLabStorageResult(bool success, bool notFound, string code, string message)
        {
            Success = success;
            NotFound = notFound;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }
        public bool NotFound { get; }
        public string Code { get; }
        public string Message { get; }

        public static TextureLabStorageResult Ok()
        {
            return new TextureLabStorageResult(true, false, string.Empty, string.Empty);
        }

        public static TextureLabStorageResult Missing(string message)
        {
            return new TextureLabStorageResult(false, true, "not_found", message);
        }

        public static TextureLabStorageResult Failed(string code, string message)
        {
            return new TextureLabStorageResult(false, false, code, message);
        }
    }

    /// <summary>
    /// Storage boundary for runtime-owned local data. Keys are logical
    /// forward-slash paths, never absolute platform paths.
    /// </summary>
    public interface ITextureLabLocalDataStore
    {
        bool TryRead(string key, out byte[] data, out TextureLabStorageResult result);

        bool TryWriteAtomic(string key, byte[] data, out TextureLabStorageResult result);

        bool TryDelete(string key, out TextureLabStorageResult result);

        bool TryList(string prefix, out IReadOnlyList<string> keys, out TextureLabStorageResult result);
    }

    /// <summary>
    /// Desktop/default runtime store. Web hosts can replace this with an
    /// adapter that flushes its virtual filesystem to browser storage.
    /// </summary>
    public sealed class TextureLabFileLocalDataStore : ITextureLabLocalDataStore
    {
        private readonly string rootPath;
        private readonly int maxBytes;

        public TextureLabFileLocalDataStore(string rootPath = null, int maxBytes = TextureLabLocalDataLimits.DefaultMaxTotalBytes)
        {
            string selectedRoot = string.IsNullOrEmpty(rootPath) ? Application.persistentDataPath : rootPath;
            this.rootPath = string.IsNullOrEmpty(selectedRoot) ? string.Empty : Path.GetFullPath(selectedRoot);
            this.maxBytes = Math.Max(1, maxBytes);
        }

        public string RootPath => rootPath;

        public bool TryRead(string key, out byte[] data, out TextureLabStorageResult result)
        {
            data = null;
            if (!TryResolveKey(key, out string path, out result))
                return false;

            try
            {
                if (!File.Exists(path))
                {
                    result = TextureLabStorageResult.Missing("The local data entry does not exist.");
                    return false;
                }

                var info = new FileInfo(path);
                if (info.Length > maxBytes)
                {
                    result = TextureLabStorageResult.Failed("size_limit", "The local data entry exceeds the storage size limit.");
                    return false;
                }

                data = File.ReadAllBytes(path);
                result = TextureLabStorageResult.Ok();
                return true;
            }
            catch (Exception exception)
            {
                result = TextureLabStorageResult.Failed("read_failed", exception.Message);
                return false;
            }
        }

        public bool TryWriteAtomic(string key, byte[] data, out TextureLabStorageResult result)
        {
            if (data == null)
            {
                result = TextureLabStorageResult.Failed("missing_data", "The local data entry is missing.");
                return false;
            }

            if (data.Length > maxBytes)
            {
                result = TextureLabStorageResult.Failed("size_limit", "The local data entry exceeds the storage size limit.");
                return false;
            }

            if (!TryResolveKey(key, out string path, out result))
                return false;

            string temporaryPath = path + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(directory))
                {
                    result = TextureLabStorageResult.Failed("path", "The local data directory is invalid.");
                    return false;
                }

                Directory.CreateDirectory(directory);
                File.WriteAllBytes(temporaryPath, data);
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temporaryPath, path, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Delete(path);
                        File.Move(temporaryPath, path);
                    }
                }
                else
                {
                    File.Move(temporaryPath, path);
                }

                result = TextureLabStorageResult.Ok();
                return true;
            }
            catch (Exception exception)
            {
                result = TextureLabStorageResult.Failed("write_failed", exception.Message);
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                    // A stale temp file must not hide the original error.
                }
            }
        }

        public bool TryDelete(string key, out TextureLabStorageResult result)
        {
            if (!TryResolveKey(key, out string path, out result))
                return false;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);

                result = TextureLabStorageResult.Ok();
                return true;
            }
            catch (Exception exception)
            {
                result = TextureLabStorageResult.Failed("delete_failed", exception.Message);
                return false;
            }
        }

        public bool TryList(string prefix, out IReadOnlyList<string> keys, out TextureLabStorageResult result)
        {
            keys = Array.Empty<string>();
            if (!TryResolveKey(prefix, out string directory, out result))
                return false;

            try
            {
                if (!Directory.Exists(directory))
                {
                    result = TextureLabStorageResult.Ok();
                    return true;
                }

                string normalizedPrefix = NormalizeKey(prefix);
                var found = new List<string>();
                foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string relative = NormalizeKey(Path.GetRelativePath(rootPath, file));
                    if (relative.StartsWith(normalizedPrefix, StringComparison.Ordinal))
                        found.Add(relative);
                }

                found.Sort(StringComparer.Ordinal);
                keys = found;
                result = TextureLabStorageResult.Ok();
                return true;
            }
            catch (Exception exception)
            {
                result = TextureLabStorageResult.Failed("list_failed", exception.Message);
                return false;
            }
        }

        private bool TryResolveKey(string key, out string path, out TextureLabStorageResult result)
        {
            path = string.Empty;
            if (string.IsNullOrEmpty(rootPath))
            {
                result = TextureLabStorageResult.Failed("root_path", "The local data root path is unavailable.");
                return false;
            }

            string rawKey = key ?? string.Empty;
            if (string.IsNullOrEmpty(rawKey) || rawKey.StartsWith("/", StringComparison.Ordinal)
                || rawKey.StartsWith("\\", StringComparison.Ordinal) || Path.IsPathRooted(rawKey))
            {
                result = TextureLabStorageResult.Failed("path", "The local data key is invalid.");
                return false;
            }

            string normalized = NormalizeKey(rawKey);
            if (string.IsNullOrEmpty(normalized) || normalized.IndexOf(':') >= 0)
            {
                result = TextureLabStorageResult.Failed("path", "The local data key is invalid.");
                return false;
            }

            foreach (string segment in normalized.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                {
                    result = TextureLabStorageResult.Failed("path", "The local data key contains an unsafe path.");
                    return false;
                }

                for (int index = 0; index < segment.Length; index++)
                {
                    if (char.IsControl(segment[index]))
                    {
                        result = TextureLabStorageResult.Failed("path", "The local data key contains a control character.");
                        return false;
                    }
                }
            }

            try
            {
                path = Path.GetFullPath(Path.Combine(rootPath, normalized.Replace('/', Path.DirectorySeparatorChar)));
                string rootWithSeparator = rootPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    ? rootPath
                    : rootPath + Path.DirectorySeparatorChar;
                if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    result = TextureLabStorageResult.Failed("path", "The local data key escapes the storage root.");
                    path = string.Empty;
                    return false;
                }
            }
            catch (Exception exception)
            {
                result = TextureLabStorageResult.Failed("path", exception.Message);
                return false;
            }

            result = TextureLabStorageResult.Ok();
            return true;
        }

        private static string NormalizeKey(string key)
        {
            return (key ?? string.Empty).Replace('\\', '/').Trim('/');
        }
    }

    internal static class TextureLabLocalDataLimits
    {
        internal const int DefaultMaxTotalBytes = 96 * 1024 * 1024;
    }
}
