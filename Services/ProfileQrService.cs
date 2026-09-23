using QRCoder;
using System.Text;

namespace GeniaProxy.Services
{
    public static class ProfileQrService
    {
        // Compact share URI should normally be far below this limit.
        // Keep a hard ceiling so an accidental huge XHTTP extra cannot
        // generate an unreadable QR again.
        public const int MaxPayloadBytes = 1_600;

        public static ProfileQrData Create(
            string profileName,
            string source)
        {
            ProfileShareLink share = ProfileShareLinkService.Create(
                profileName,
                source
            );

            int payloadBytes = Encoding.UTF8.GetByteCount(share.Payload);

            if (payloadBytes > MaxPayloadBytes)
            {
                throw new InvalidDataException(
                    "Мобильная ссылка слишком большая для удобного QR-кода " +
                    $"({payloadBytes} байт; предел {MaxPayloadBytes}). " +
                    "Передайте ссылку или JSON-файл напрямую."
                );
            }

            byte[] png = PngByteQRCodeHelper.GetQRCode(
                share.Payload,
                QRCodeGenerator.ECCLevel.M,
                10
            );

            if (png.Length < 8 ||
                png[0] != 0x89 ||
                png[1] != 0x50 ||
                png[2] != 0x4E ||
                png[3] != 0x47)
            {
                throw new InvalidDataException(
                    "Не удалось сформировать PNG QR-кода."
                );
            }

            return new ProfileQrData(
                share.ProfileName,
                share.Payload,
                payloadBytes,
                png,
                share.DisplayFormat,
                share.Scheme
            );
        }
    }

    public sealed record ProfileQrData(
        string ProfileName,
        string Payload,
        int PayloadBytes,
        byte[] PngBytes,
        string DisplayFormat,
        string Scheme
    );
}
