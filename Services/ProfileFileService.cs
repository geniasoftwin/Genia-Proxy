namespace GeniaProxy.Services
{
    public sealed class ProfileFileService
    {
        private readonly string profilesDirectory;

        public ProfileFileService(string profilesDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                profilesDirectory
            );

            this.profilesDirectory = Path.GetFullPath(
                profilesDirectory
            );
        }

        public void Rename(string currentName, string newName)
        {
            string sourceName = ValidateName(currentName);
            string destinationName = ValidateName(newName);

            string sourcePath = GetProfilePath(sourceName);
            string destinationPath = GetProfilePath(destinationName);

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    "Переименовываемый профиль не найден.",
                    sourcePath
                );
            }

            if (string.Equals(
                    sourceName,
                    destinationName,
                    StringComparison.Ordinal))
            {
                return;
            }

            bool caseOnlyRename = string.Equals(
                sourceName,
                destinationName,
                StringComparison.OrdinalIgnoreCase
            );

            if (!caseOnlyRename && File.Exists(destinationPath))
            {
                throw new IOException(
                    $"Профиль «{destinationName}» уже существует."
                );
            }

            Directory.CreateDirectory(profilesDirectory);

            if (!caseOnlyRename)
            {
                File.Move(sourcePath, destinationPath);
                return;
            }

            string temporaryPath = Path.Combine(
                profilesDirectory,
                $".rename-{Guid.NewGuid():N}.tmp"
            );

            try
            {
                File.Move(sourcePath, temporaryPath);
                File.Move(temporaryPath, destinationPath);
            }
            catch
            {
                if (File.Exists(temporaryPath) &&
                    !File.Exists(sourcePath))
                {
                    File.Move(temporaryPath, sourcePath);
                }

                throw;
            }
        }

        public void Delete(string profileName)
        {
            string normalizedName = ValidateName(profileName);
            string path = GetProfilePath(normalizedName);

            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Удаляемый профиль не найден.",
                    path
                );
            }

            File.Delete(path);
        }

        private string GetProfilePath(string profileName)
        {
            return Path.Combine(
                profilesDirectory,
                profileName + ".json"
            );
        }

        private static string ValidateName(string profileName)
        {
            string normalizedName = ProfileNameValidator.Normalize(
                profileName
            );

            string? error = ProfileNameValidator.GetValidationError(
                normalizedName
            );

            if (error is not null)
            {
                throw new InvalidDataException(error);
            }

            return normalizedName;
        }
    }
}
