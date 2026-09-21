using System.Data;
using System.Security.Claims;
using MAM.Application.Administration;
using MAM.Application.Auditing;
using MAM.Application.Identity;
using MAM.Infrastructure.Catalog;
using Microsoft.Data.SqlClient;

namespace MAM.Api;

public static class P1214NavigationEndpoints
{
    private static readonly IReadOnlyDictionary<string, NavigationDefinition> Definitions =
        new Dictionary<string, NavigationDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["dashboard"] = new("dashboard", null, "route"),
            ["library"] = new("library", null, "route"),
            ["curation-actions"] = new("curation-actions", null, "route"),
            ["ingest"] = new("ingest", null, "route"),
            ["upload"] = new("upload", null, "route"),
            ["queue"] = new("queue", null, "route"),
            ["reports"] = new("reports", null, "route"),
            ["tapes"] = new("tapes", null, "route"),
            ["protection"] = new("protection", null, "route"),
            ["system-admin"] = new(null, null, "group"),
            ["systemFunctions"] = new("systemFunctions", null, "route"),
            ["admin"] = new("admin", "system-admin", "route"),
            ["settings"] = new("settings", "system-admin", "route"),
            ["categories"] = new("categories", "system-admin", "route"),
            ["references"] = new("references", "system-admin", "route"),
            ["mediaPermissions"] = new("mediaPermissions", "system-admin", "route"),
            ["capabilities"] = new("capabilities", null, "route"),
            ["admin-actions"] = new("admin-actions", null, "route"),
            ["search"] = new("search", null, "route")
        };

    public static void Map(WebApplication app, string configuredApiBasePath)
    {
        var group = app.MapGroup($"{configuredApiBasePath}/v1/admin/navigation");

        group.MapGet("", async (SqlServerConnectionFactory connections, CancellationToken cancellationToken) =>
        {
            try
            {
                await using var connection = await connections.OpenAsync(cancellationToken);
                const string sql = """
                    SELECT NavigationKey,RouteKey,ParentKey,ItemType,LabelEn,LabelAr,IsEnabled,SortOrder,UpdatedAtUtc,UpdatedBy
                    FROM dbo.MamNavigationItem
                    ORDER BY CASE WHEN ParentKey IS NULL THEN 0 ELSE 1 END, ParentKey, SortOrder, NavigationKey;
                    """;
                await using var command = new SqlCommand(sql, connection) { CommandTimeout = connections.CommandTimeoutSeconds };
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                var items = new List<NavigationItemDto>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    items.Add(new NavigationItemDto(
                        reader.GetString(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetString(5),
                        reader.GetBoolean(6),
                        reader.GetInt32(7),
                        reader.GetDateTime(8),
                        reader.IsDBNull(9) ? null : reader.GetString(9)));
                }
                return Results.Ok(new { items });
            }
            catch (SqlException ex) when (ex.Number == 208)
            {
                return Results.Json(new { error = "navigation_configuration_unavailable", detail = "Navigation configuration migration is not applied." }, statusCode: 503);
            }
        }).RequireAuthorization();

        group.MapPut("", async (
            NavigationConfigurationUpdateRequest request,
            ClaimsPrincipal principal,
            SqlServerConnectionFactory connections,
            IAuditSink audit,
            CancellationToken cancellationToken) =>
        {
            var items = request.Items ?? Array.Empty<NavigationItemUpdateRequest>();
            if (items.Length == 0)
                return Results.BadRequest(new { error = "invalid_navigation_configuration", detail = "At least one valid navigation item is required." });

            var duplicate = items.GroupBy(x => x.NavigationKey ?? string.Empty, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
            if (duplicate is not null)
                return Results.BadRequest(new { error = "duplicate_navigation_key", detail = $"Navigation key '{duplicate.Key}' appears more than once." });

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.NavigationKey) || !Definitions.ContainsKey(item.NavigationKey))
                    return Results.BadRequest(new { error = "invalid_navigation_key", detail = $"Navigation key '{item.NavigationKey}' is not supported." });
                if (string.IsNullOrWhiteSpace(item.LabelEn) || item.LabelEn.Trim().Length > 100)
                    return Results.BadRequest(new { error = "invalid_navigation_label", detail = $"English label for '{item.NavigationKey}' must contain 1-100 characters." });
                if (string.IsNullOrWhiteSpace(item.LabelAr) || item.LabelAr.Trim().Length > 100)
                    return Results.BadRequest(new { error = "invalid_navigation_label", detail = $"Arabic label for '{item.NavigationKey}' must contain 1-100 characters." });
                if (item.LabelEn.Any(char.IsControl) || item.LabelAr.Any(char.IsControl))
                    return Results.BadRequest(new { error = "invalid_navigation_label", detail = $"Labels for '{item.NavigationKey}' contain invalid control characters." });
                if (item.SortOrder is < 0 or > 9999)
                    return Results.BadRequest(new { error = "invalid_navigation_order", detail = $"Sort order for '{item.NavigationKey}' must be between 0 and 9999." });
            }

            var actor = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.Identity?.Name ?? "unknown";
            var now = DateTimeOffset.UtcNow;
            await using var connection = await connections.OpenAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                foreach (var item in items)
                {
                    var key = item.NavigationKey.Trim();
                    var definition = Definitions[key];
                    const string sql = """
                        MERGE dbo.MamNavigationItem AS target
                        USING (SELECT @NavigationKey NavigationKey) AS source
                          ON target.NavigationKey=source.NavigationKey
                        WHEN MATCHED THEN
                          UPDATE SET RouteKey=@RouteKey,ParentKey=@ParentKey,ItemType=@ItemType,
                                     LabelEn=@LabelEn,LabelAr=@LabelAr,IsEnabled=@IsEnabled,
                                     SortOrder=@SortOrder,UpdatedAtUtc=@UpdatedAtUtc,UpdatedBy=@UpdatedBy
                        WHEN NOT MATCHED THEN
                          INSERT(NavigationKey,RouteKey,ParentKey,ItemType,LabelEn,LabelAr,IsEnabled,SortOrder,UpdatedAtUtc,UpdatedBy)
                          VALUES(@NavigationKey,@RouteKey,@ParentKey,@ItemType,@LabelEn,@LabelAr,@IsEnabled,@SortOrder,@UpdatedAtUtc,@UpdatedBy);
                        """;
                    await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = connections.CommandTimeoutSeconds };
                    command.Parameters.Add("@NavigationKey", SqlDbType.NVarChar, 64).Value = key;
                    command.Parameters.Add("@RouteKey", SqlDbType.NVarChar, 64).Value = (object?)definition.RouteKey ?? DBNull.Value;
                    command.Parameters.Add("@ParentKey", SqlDbType.NVarChar, 64).Value = (object?)definition.ParentKey ?? DBNull.Value;
                    command.Parameters.Add("@ItemType", SqlDbType.NVarChar, 16).Value = definition.ItemType;
                    command.Parameters.Add("@LabelEn", SqlDbType.NVarChar, 100).Value = item.LabelEn.Trim();
                    command.Parameters.Add("@LabelAr", SqlDbType.NVarChar, 100).Value = item.LabelAr.Trim();
                    command.Parameters.Add("@IsEnabled", SqlDbType.Bit).Value = item.IsEnabled;
                    command.Parameters.Add("@SortOrder", SqlDbType.Int).Value = item.SortOrder;
                    command.Parameters.Add("@UpdatedAtUtc", SqlDbType.DateTime2).Value = now.UtcDateTime;
                    command.Parameters.Add("@UpdatedBy", SqlDbType.NVarChar, 200).Value = actor.Length <= 200 ? actor : actor[..200];
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }

            await audit.AppendAsync(new AuditEvent(
                Guid.NewGuid(), now, actor, "administration.navigation.updated", "NavigationConfiguration", "global", "Success",
                $"items={items.Length}"), cancellationToken);

            return Results.Ok(new { updated = items.Length, updatedAtUtc = now });
        }).RequireAuthorization(MamSecurity.AdministrationPolicy);
    }

    private sealed record NavigationDefinition(string? RouteKey, string? ParentKey, string ItemType);

    public sealed record NavigationConfigurationUpdateRequest(NavigationItemUpdateRequest[]? Items);
    public sealed record NavigationItemUpdateRequest(string NavigationKey, string LabelEn, string LabelAr, bool IsEnabled, int SortOrder);
    public sealed record NavigationItemDto(
        string NavigationKey,
        string? RouteKey,
        string? ParentKey,
        string ItemType,
        string LabelEn,
        string LabelAr,
        bool IsEnabled,
        int SortOrder,
        DateTime UpdatedAtUtc,
        string? UpdatedBy);
}
