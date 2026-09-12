namespace MAM.Application.Metadata;

public sealed record MetadataFieldDefinition(
    string Key,
    string DisplayNameEn,
    string DisplayNameAr,
    string DataType,
    bool Required,
    int? MaxLength = null);

public sealed record MetadataSchemaTemplate(
    string Key,
    int Version,
    string DisplayNameEn,
    string DisplayNameAr,
    IReadOnlyList<MetadataFieldDefinition> Fields);

public sealed record MetadataValidationError(string FieldKey, string Message);

public interface IMetadataSchemaRegistry
{
    IReadOnlyList<MetadataSchemaTemplate> List();
    MetadataSchemaTemplate? Get(string key);
    IReadOnlyList<MetadataValidationError> Validate(string key, IReadOnlyDictionary<string, string?> values);
}

public sealed class BuiltInMetadataSchemaRegistry : IMetadataSchemaRegistry
{
    public const string CoreMediaSchemaKey = "core-media-v1";

    private static readonly MetadataSchemaTemplate CoreMedia = new(
        CoreMediaSchemaKey,
        1,
        "Core Media Metadata",
        "البيانات الوصفية الأساسية للوسائط",
        new MetadataFieldDefinition[]
        {
            new("title", "Title", "العنوان", "text", true, 300),
            new("titleAr", "Arabic title", "العنوان العربي", "text", false, 300),
            new("eventDate", "Event date", "تاريخ الحدث", "date", false),
            new("category", "Category", "التصنيف", "text", false, 120),
            new("tags", "Tags", "الوسوم", "text", false, 1000),
            new("preservationNotes", "Preservation notes", "ملاحظات الحفظ", "text", false, 2000)
        });

    private static readonly IReadOnlyList<MetadataSchemaTemplate> Schemas = new[] { CoreMedia };

    public IReadOnlyList<MetadataSchemaTemplate> List() => Schemas;

    public MetadataSchemaTemplate? Get(string key) =>
        Schemas.FirstOrDefault(schema => string.Equals(schema.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<MetadataValidationError> Validate(string key, IReadOnlyDictionary<string, string?> values)
    {
        var schema = Get(key);
        if (schema is null) return new[] { new MetadataValidationError("$schema", "Unknown metadata schema.") };

        values ??= new Dictionary<string, string?>();
        var errors = new List<MetadataValidationError>();
        var known = schema.Fields.Select(field => field.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var supplied in values.Keys.Where(keyName => !known.Contains(keyName)))
            errors.Add(new MetadataValidationError(supplied, "Field is not defined by the selected metadata schema."));

        foreach (var field in schema.Fields)
        {
            values.TryGetValue(field.Key, out var value);
            var normalized = value?.Trim();
            if (field.Required && string.IsNullOrEmpty(normalized))
            {
                errors.Add(new MetadataValidationError(field.Key, "Field is required."));
                continue;
            }
            if (field.MaxLength is int maxLength && normalized is { Length: > 0 } && normalized.Length > maxLength)
                errors.Add(new MetadataValidationError(field.Key, $"Field cannot exceed {maxLength} characters."));
            if (string.Equals(field.DataType, "date", StringComparison.OrdinalIgnoreCase) &&
                normalized is { Length: > 0 } && !DateOnly.TryParse(normalized, out _))
                errors.Add(new MetadataValidationError(field.Key, "Field must be a valid date."));
        }
        return errors;
    }
}
