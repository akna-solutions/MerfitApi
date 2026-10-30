using System.Text.Json;
using System.Text.Json.Serialization;

namespace MerfitApi.Api.Json;

/// <summary>
/// Istek govdelerindeki enum alanlarini hem sayi (0, 1, ...) hem de ad ("Active", "announcement") olarak kabul eder.
/// Yanitlar eskisi gibi sayi olarak yazilir; boylece enum'u sayi bekleyen mevcut istemciler (mobil uygulama,
/// admin panelindeki apiEnums eslemeleri) etkilenmez. Admin panelinin bildirim ekranlari enum'lari ad olarak
/// gonderdigi icin bu donusturucu olmadan istekler "The request field is required." ile 400 donuyordu.
/// </summary>
public sealed class LenientEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(LenientEnumConverter<>).MakeGenericType(typeToConvert))!;

    private sealed class LenientEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number when reader.TryGetInt64(out var number):
                    return (TEnum)Enum.ToObject(typeof(TEnum), number);

                case JsonTokenType.String:
                    var text = reader.GetString();
                    // Sayisal metinleri ("1") reddet: Enum.TryParse bunlari tanimsiz degerlere de cevirebilir.
                    if (!string.IsNullOrWhiteSpace(text)
                        && !char.IsDigit(text.TrimStart()[0])
                        && text.TrimStart()[0] != '-'
                        && Enum.TryParse<TEnum>(text, ignoreCase: true, out var value))
                    {
                        return value;
                    }

                    throw new JsonException($"'{text}' gecerli bir {typeof(TEnum).Name} degeri degil.");

                default:
                    throw new JsonException($"{typeof(TEnum).Name} icin sayi veya metin bekleniyordu.");
            }
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(Convert.ToInt64(value));
    }
}
