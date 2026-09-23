using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GeniaProxy.Services
{
    public sealed class ProfileBackupService
    {
        private const long MaxEntrySize = 5 * 1024 * 1024;
        private const long MaxProfileSize =
            JsonProfileImportService.MaxProfileBytes;
        private const long MaxSettingsSize = 256 * 1024;
        private const long MaxArchiveContentSize =
            20 * 1024 * 1024;
        private const int MaxArchiveEntries = 1_000;
        private const int MaxProfiles = 500;

        private static readonly JsonSerializerOptions
            ManifestJsonOptions = new()
            {
                WriteIndented = true
            };

        private readonly string profilesDirectory;
        private readonly string settingsPath;

        public ProfileBackupService(
            string profilesDirectory,
            string settingsPath)
        {
            this.profilesDirectory =
                Path.GetFullPath(profilesDirectory);

            this.settingsPath =
                Path.GetFullPath(settingsPath);
        }

        public BackupExportResult Export(string archivePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                archivePath
            );

            string fullArchivePath =
                Path.GetFullPath(archivePath);

            string? outputDirectory =
                Path.GetDirectoryName(fullArchivePath);

            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            string temporaryPath = fullArchivePath +
                $".{Guid.NewGuid():N}.tmp";

            int profileCount = 0;

            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                using (var archive = new ZipArchive(
                           stream,
                           ZipArchiveMode.Create))
                {
                    WriteTextEntry(
                        archive,
                        "manifest.json",
                        JsonSerializer.Serialize(
                            new BackupManifest(
                                FormatVersion: 1,
                                Application: "GeniaProxy",
                                CreatedUtc: DateTime.UtcNow
                            ),
                            ManifestJsonOptions
                        )
                    );

                    if (Directory.Exists(profilesDirectory))
                    {
                        foreach (string path in Directory
                                     .GetFiles(
                                         profilesDirectory,
                                         "*.json")
                                     .Where(path =>
                                         !string.Equals(
                                             Path.GetFileName(path),
                                             "_active.runtime",
                                             StringComparison
                                                 .OrdinalIgnoreCase))
                                     .OrderBy(
                                         Path.GetFileName,
                                         StringComparer
                                             .OrdinalIgnoreCase))
                        {
                            string name = Path
                                .GetFileNameWithoutExtension(path);

                            if (ProfileNameValidator
                                    .GetValidationError(name)
                                is not null)
                            {
                                continue;
                            }

                            if (new FileInfo(path).Length >
                                MaxProfileSize)
                            {
                                throw new InvalidDataException(
                                    $"Профиль «{name}» превышает " +
                                    "допустимый размер 1 МБ."
                                );
                            }

                            string json = File.ReadAllText(
                                path,
                                Encoding.UTF8
                            );

                            ValidateJsonObject(
                                json,
                                $"профиль {name}"
                            );

                            WriteTextEntry(
                                archive,
                                $"profiles/{name}.json",
                                json
                            );

                            profileCount++;
                        }
                    }

                    if (File.Exists(settingsPath))
                    {
                        if (new FileInfo(settingsPath).Length >
                            MaxSettingsSize)
                        {
                            throw new InvalidDataException(
                                "Файл настроек слишком большой."
                            );
                        }

                        string settings = File.ReadAllText(
                            settingsPath,
                            Encoding.UTF8
                        );

                        ValidateJsonObject(
                            settings,
                            "настройки"
                        );

                        WriteTextEntry(
                            archive,
                            "settings.json",
                            settings
                        );
                    }
                }

                File.Move(
                    temporaryPath,
                    fullArchivePath,
                    overwrite: true
                );
            }
            finally
            {
                TryDelete(temporaryPath);
            }

            return new BackupExportResult(
                profileCount,
                File.Exists(settingsPath)
            );
        }

        public BackupRestoreResult Restore(
            string archivePath,
            BackupRestoreMode mode)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                archivePath
            );

            if (!File.Exists(archivePath))
            {
                throw new FileNotFoundException(
                    "Архив резервной копии не найден.",
                    archivePath
                );
            }

            var profiles =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase
                );

            string? settings = null;
            long totalSize = 0;

            using (ZipArchive archive = ZipFile.OpenRead(
                       archivePath))
            {
                if (archive.Entries.Count > MaxArchiveEntries)
                {
                    throw new InvalidDataException(
                        "В резервной копии слишком много файлов."
                    );
                }

                ZipArchiveEntry? manifestEntry =
                    archive.GetEntry("manifest.json");

                if (manifestEntry is null)
                {
                    throw new InvalidDataException(
                        "Это не резервная копия GeniaProxy: " +
                        "отсутствует manifest.json."
                    );
                }

                if (manifestEntry.Length > MaxEntrySize)
                {
                    throw new InvalidDataException(
                        "Файл manifest.json слишком большой."
                    );
                }

                BackupManifest manifest =
                    ReadManifest(manifestEntry);

                if (manifest.FormatVersion != 1 ||
                    !string.Equals(
                        manifest.Application,
                        "GeniaProxy",
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Формат резервной копии не поддерживается."
                    );
                }

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.Length > MaxEntrySize)
                    {
                        throw new InvalidDataException(
                            $"Файл {entry.FullName} слишком большой."
                        );
                    }

                    totalSize += entry.Length;

                    if (totalSize > MaxArchiveContentSize)
                    {
                        throw new InvalidDataException(
                            "Содержимое резервной копии " +
                            "превышает допустимый размер."
                        );
                    }

                    if (entry.FullName.StartsWith(
                            "profiles/",
                            StringComparison.Ordinal) &&
                        entry.FullName.EndsWith(
                            ".json",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        if (entry.Length > MaxProfileSize)
                        {
                            throw new InvalidDataException(
                                $"Профиль {entry.FullName} превышает " +
                                "допустимый размер 1 МБ."
                            );
                        }

                        string fileName = Path.GetFileName(
                            entry.FullName
                        );

                        if (!string.Equals(
                                entry.FullName,
                                $"profiles/{fileName}",
                                StringComparison.Ordinal))
                        {
                            throw new InvalidDataException(
                                "В архиве обнаружен недопустимый " +
                                "путь профиля."
                            );
                        }

                        string profileName = Path
                            .GetFileNameWithoutExtension(fileName);

                        string? validationError =
                            ProfileNameValidator
                                .GetValidationError(profileName);

                        if (validationError is not null)
                        {
                            throw new InvalidDataException(
                                $"Недопустимое имя профиля " +
                                $"«{profileName}»: {validationError}"
                            );
                        }

                        string json = ReadTextEntry(entry);

                        ValidateJsonObject(
                            json,
                            $"профиль {profileName}"
                        );

                        if (!profiles.TryAdd(
                                profileName,
                                json))
                        {
                            throw new InvalidDataException(
                                $"Профиль «{profileName}» " +
                                "дублируется в архиве."
                            );
                        }

                        if (profiles.Count > MaxProfiles)
                        {
                            throw new InvalidDataException(
                                $"В резервной копии слишком много " +
                                $"профилей (максимум {MaxProfiles})."
                            );
                        }
                    }
                    else if (string.Equals(
                                 entry.FullName,
                                 "settings.json",
                                 StringComparison.Ordinal))
                    {
                        if (entry.Length > MaxSettingsSize)
                        {
                            throw new InvalidDataException(
                                "Файл настроек в архиве " +
                                "слишком большой."
                            );
                        }

                        if (settings is not null)
                        {
                            throw new InvalidDataException(
                                "Файл settings.json дублируется " +
                                "в резервной копии."
                            );
                        }

                        settings = ReadTextEntry(entry);
                        ValidateJsonObject(settings, "настройки");
                    }
                }
            }

            return ApplyRestoreTransaction(
                profiles,
                settings,
                mode
            );
        }

        private BackupRestoreResult ApplyRestoreTransaction(
            IReadOnlyDictionary<string, string> profiles,
            string? settings,
            BackupRestoreMode mode)
        {
            Directory.CreateDirectory(profilesDirectory);

            string rollbackDirectory = Path.Combine(
                profilesDirectory,
                $".restore-{Guid.NewGuid():N}"
            );

            string settingsBackupPath = settingsPath +
                $".{Guid.NewGuid():N}.restore";

            var writtenProfiles = new List<string>();
            var movedProfiles =
                new List<(string Backup, string Original)>();

            bool settingsExisted = File.Exists(settingsPath);
            bool settingsWritten = false;
            int restored = 0;
            int skipped = 0;
            bool success = false;

            try
            {
                if (mode == BackupRestoreMode.Replace)
                {
                    Directory.CreateDirectory(
                        rollbackDirectory
                    );

                    foreach (string existing in Directory
                                 .GetFiles(
                                     profilesDirectory,
                                     "*.json"))
                    {
                        if (string.Equals(
                                Path.GetFileName(existing),
                                "_active.runtime",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string backup = Path.Combine(
                            rollbackDirectory,
                            Path.GetFileName(existing)
                        );

                        File.Move(existing, backup);
                        movedProfiles.Add((backup, existing));
                    }
                }

                foreach ((string name, string json) in profiles)
                {
                    string destination = Path.Combine(
                        profilesDirectory,
                        name + ".json"
                    );

                    if (mode == BackupRestoreMode.Merge &&
                        File.Exists(destination))
                    {
                        skipped++;
                        continue;
                    }

                    AtomicFileWriter.WriteAllText(
                        destination,
                        json,
                        new UTF8Encoding(
                            encoderShouldEmitUTF8Identifier: false
                        )
                    );

                    writtenProfiles.Add(destination);
                    restored++;
                }

                if (settings is not null)
                {
                    if (settingsExisted)
                    {
                        File.Copy(
                            settingsPath,
                            settingsBackupPath
                        );
                    }

                    AtomicFileWriter.WriteAllText(
                        settingsPath,
                        settings,
                        new UTF8Encoding(
                            encoderShouldEmitUTF8Identifier: false
                        )
                    );

                    settingsWritten = true;
                }

                success = true;

                return new BackupRestoreResult(
                    restored,
                    skipped,
                    settingsWritten
                );
            }
            finally
            {
                if (!success)
                {
                    foreach (string path in writtenProfiles)
                    {
                        TryDelete(path);
                    }

                    foreach ((string backup, string original)
                             in movedProfiles)
                    {
                        if (File.Exists(backup))
                        {
                            File.Move(
                                backup,
                                original,
                                overwrite: true
                            );
                        }
                    }

                    if (settingsWritten)
                    {
                        if (settingsExisted &&
                            File.Exists(settingsBackupPath))
                        {
                            File.Move(
                                settingsBackupPath,
                                settingsPath,
                                overwrite: true
                            );
                        }
                        else
                        {
                            TryDelete(settingsPath);
                        }
                    }
                }

                TryDelete(settingsBackupPath);
                TryDeleteDirectory(rollbackDirectory);
            }
        }

        private static BackupManifest ReadManifest(
            ZipArchiveEntry entry)
        {
            string json = ReadTextEntry(entry);

            return JsonSerializer.Deserialize<BackupManifest>(
                       json
                   )
                   ?? throw new InvalidDataException(
                       "Файл manifest.json повреждён."
                   );
        }

        private static string ReadTextEntry(
            ZipArchiveEntry entry)
        {
            using Stream stream = entry.Open();
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 1024,
                leaveOpen: false
            );

            return reader.ReadToEnd();
        }

        private static void WriteTextEntry(
            ZipArchive archive,
            string entryName,
            string text)
        {
            ZipArchiveEntry entry = archive.CreateEntry(
                entryName,
                CompressionLevel.Optimal
            );

            using Stream stream = entry.Open();
            using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false
                ),
                bufferSize: 1024,
                leaveOpen: false
            );

            writer.Write(text);
        }

        private static void ValidateJsonObject(
            string json,
            string description)
        {
            try
            {
                if (JsonNode.Parse(json) is not JsonObject)
                {
                    throw new InvalidDataException(
                        $"Файл «{description}» не является " +
                        "JSON-объектом."
                    );
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"Файл «{description}» содержит " +
                    "некорректный JSON.",
                    ex
                );
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Не скрываем результат успешного экспорта.
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // Содержимое уже восстановлено или применено.
            }
        }

        private sealed record BackupManifest(
            int FormatVersion,
            string Application,
            DateTime CreatedUtc
        );
    }

    public enum BackupRestoreMode
    {
        Merge,
        Replace
    }

    public sealed record BackupExportResult(
        int ProfilesExported,
        bool SettingsExported
    );

    public sealed record BackupRestoreResult(
        int ProfilesRestored,
        int ProfilesSkipped,
        bool SettingsRestored
    );
}
