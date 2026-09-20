using System.Security.Claims;
using MAM.Application.Auditing;
using MAM.Application.Identity;
using MAM.Application.Discovery;
using MAM.Application.Processing;
using MAM.Application.SystemFunctions;
using MAM.Application.Tapes;
using MAM.Infrastructure.Catalog;
using MAM.Infrastructure.Demo;
using MAM.Infrastructure.Tapes;

namespace MAM.Api;

public static class T2TapeInventoryEndpoints
{
    public static void Map(WebApplication app, string configuredApiBasePath)
    {
        var api = app.MapGroup($"{configuredApiBasePath}/v1/tapes");

        api.MapGet("/", async (int? limit, IServiceProvider services, CancellationToken ct) =>
        {
            try
            {
                if (!await Feature(services,MamSystemFunctionKeys.TapeManagement,ct)) return Disabled(MamSystemFunctionKeys.TapeManagement);
                return Results.Ok(await Resolve(services).ListAsync(null,limit??250,ct));
            }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
            catch (SystemFunctionRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.TapeViewPolicy);

        api.MapGet("/search", async (string? query, int? limit, IServiceProvider services, CancellationToken ct) =>
        {
            try
            {
                if (!await Feature(services,MamSystemFunctionKeys.TapeManagement,ct)) return Disabled(MamSystemFunctionKeys.TapeManagement);
                return Results.Ok(await Resolve(services).ListAsync(query,limit??250,ct));
            }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
            catch (SystemFunctionRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.TapeSearchPolicy);

        api.MapGet("/content-search", async (string? query, int? limit, IServiceProvider services, CancellationToken ct) =>
        {
            try
            {
                if (!await Feature(services,MamSystemFunctionKeys.TapeSearchInContent,ct)) return Disabled(MamSystemFunctionKeys.TapeSearchInContent);
                return Results.Ok(await Resolve(services).ListAsync(query,limit??100,ct));
            }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
            catch (SystemFunctionRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.TapeSearchPolicy);

        api.MapGet("/{tapeId:guid}", async (Guid tapeId, IServiceProvider services, CancellationToken ct) =>
        {
            try
            {
                if (!await Feature(services,MamSystemFunctionKeys.TapeManagement,ct)) return Disabled(MamSystemFunctionKeys.TapeManagement);
                var item=await Resolve(services).GetAsync(tapeId,ct);
                return item is null?Results.NotFound(new{error="tape_not_found"}):Results.Ok(item);
            }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
            catch (SystemFunctionRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.TapeViewPolicy);

        api.MapGet("/resolve/{*scanValue}", async (string scanValue, IServiceProvider services, CancellationToken ct) =>
        {
            try
            {
                if (!await Feature(services,MamSystemFunctionKeys.TapeManagement,ct)) return Disabled(MamSystemFunctionKeys.TapeManagement);
                var item=await Resolve(services).ResolveCodeAsync(scanValue,ct);
                return item is null?Results.NotFound(new{error="tape_not_found"}):Results.Ok(item);
            }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
            catch (SystemFunctionRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.TapeSearchPolicy);

        api.MapGet("/{tapeId:guid}/barcode", async (Guid tapeId, IServiceProvider services, CancellationToken ct) =>
        {
            try
            {
                if (!await Feature(services,MamSystemFunctionKeys.TapeManagement,ct)) return Disabled(MamSystemFunctionKeys.TapeManagement);
                var item=await Resolve(services).GetAsync(tapeId,ct);
                return item is null?Results.NotFound(new{error="tape_not_found"}):Results.Ok(TapeBarcodePayload.For(item));
            }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
            catch (SystemFunctionRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.TapeViewPolicy);

        api.MapPost("/{tapeId:guid}/print-events", async (
            Guid tapeId,
            TapePrintEventRequest request,
            ClaimsPrincipal principal,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            try
            {
                if (!await Feature(services,MamSystemFunctionKeys.TapePrinting,ct)) return Disabled(MamSystemFunctionKeys.TapePrinting);
                var item=await Resolve(services).GetAsync(tapeId,ct);
                if(item is null)return Results.NotFound(new{error="tape_not_found"});
                await Config(services).RecordPrintEventAsync(item,request,Actor(principal),ct);
                return Results.Ok(new{recorded=true,tapeId,item.TapeCode});
            }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
            catch (SystemFunctionRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.TapePrintPolicy);

        api.MapPost("/", async (CreateTapeRequest request, ClaimsPrincipal principal, IServiceProvider services, CancellationToken ct) =>
        {
            try
            {
                if (!await Feature(services,MamSystemFunctionKeys.TapeManagement,ct)) return Disabled(MamSystemFunctionKeys.TapeManagement);
                var departmentError=await ValidateDepartmentAsync(request.OwnerDepartment,services,ct);
                if(departmentError is not null)return departmentError;
                var created=await Resolve(services).CreateAsync(request,Actor(principal),ct);
                return Results.Created($"{configuredApiBasePath}/v1/tapes/{created.TapeId:D}",created);
            }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
            catch (SystemFunctionRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.TapeCreatePolicy);

        api.MapPut("/{tapeId:guid}", async (Guid tapeId, UpdateTapeRequest request, ClaimsPrincipal principal, IServiceProvider services, CancellationToken ct) =>
        {
            try
            {
                if (!await Feature(services,MamSystemFunctionKeys.TapeManagement,ct)) return Disabled(MamSystemFunctionKeys.TapeManagement);
                var departmentError=await ValidateDepartmentAsync(request.OwnerDepartment,services,ct);
                if(departmentError is not null)return departmentError;
                return Results.Ok(await Resolve(services).UpdateAsync(tapeId,request,Actor(principal),ct));
            }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
            catch (SystemFunctionRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.TapeEditPolicy);

        api.MapDelete("/{tapeId:guid}", async (Guid tapeId, int expectedVersion, ClaimsPrincipal principal, IServiceProvider services, CancellationToken ct) =>
        {
            try
            {
                if (!await Feature(services,MamSystemFunctionKeys.TapeManagement,ct)) return Disabled(MamSystemFunctionKeys.TapeManagement);
                await Resolve(services).DeleteAsync(tapeId,expectedVersion,Actor(principal),ct);
                return Results.NoContent();
            }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
            catch (SystemFunctionRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.TapeDeletePolicy);

        api.MapGet("/{tapeId:guid}/attachments", async (Guid tapeId, IServiceProvider services, CancellationToken ct) =>
        {
            try
            {
                if (!await Feature(services,MamSystemFunctionKeys.TapeManagement,ct)) return Disabled(MamSystemFunctionKeys.TapeManagement);
                return Results.Ok(await Attachments(services).ListAsync(tapeId,ct));
            }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
            catch (SystemFunctionRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.TapeViewPolicy);

        api.MapPost("/{tapeId:guid}/attachments", async (
            Guid tapeId,
            LinkTapeAttachmentRequest request,
            ClaimsPrincipal principal,
            IMediaProcessingService processing,
            IDiscoveryService discovery,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            TapeAttachmentItem? attachment=null;
            var actor=Actor(principal);
            try
            {
                if (!await Feature(services,MamSystemFunctionKeys.TapeManagement,ct)) return Disabled(MamSystemFunctionKeys.TapeManagement);
                attachment=await Attachments(services).LinkAsync(tapeId,request,actor,ct);
                var job=await processing.EnqueueAsync(attachment.AssetId,BuiltInProcessingProfiles.OcrText,actor,ct);
                try
                {
                    await discovery.SetExtractionStatusAsync(
                        attachment.AssetId,DiscoverySources.Ocr,"Queued",0,
                        "OCR queued automatically for tape attachment.",false,ct);
                }
                catch { /* The processing job is authoritative; worker will publish extraction status. */ }

                return Results.Created(
                    $"{configuredApiBasePath}/v1/tapes/{tapeId:D}/attachments/{attachment.AttachmentId:D}",
                    new { attachment, ocrJob=job, ocrProfile=BuiltInProcessingProfiles.OcrText });
            }
            catch (ProcessingRequestException ex)
            {
                if(attachment is not null)
                {
                    try { await Attachments(services).DeleteAsync(tapeId,attachment.AttachmentId,actor,CancellationToken.None); } catch { }
                }
                return Results.Json(new { error=ex.Code,detail=ex.Message },statusCode:ex.StatusCode);
            }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
            catch (SystemFunctionRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.TapeEditPolicy);

        api.MapDelete("/{tapeId:guid}/attachments/{attachmentId:guid}", async (
            Guid tapeId,
            Guid attachmentId,
            ClaimsPrincipal principal,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            try
            {
                if (!await Feature(services,MamSystemFunctionKeys.TapeManagement,ct)) return Disabled(MamSystemFunctionKeys.TapeManagement);
                await Attachments(services).DeleteAsync(tapeId,attachmentId,Actor(principal),ct);
                return Results.NoContent();
            }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
            catch (SystemFunctionRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.TapeEditPolicy);

        api.MapGet("/formats/list", async (bool? includeInactive, IServiceProvider services, CancellationToken ct) =>
        {
            try { return Results.Ok(await Resolve(services).ListFormatsAsync(includeInactive==true,ct)); }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.TapeViewPolicy);

        api.MapPut("/formats/{code}", async (string code, UpsertTapeFormatRequest request, ClaimsPrincipal principal, IServiceProvider services, CancellationToken ct) =>
        {
            if(!string.Equals(code,request.Code,StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new{error="format_code_mismatch",detail="Route and body format codes must match."});
            try { return Results.Ok(await Resolve(services).UpsertFormatAsync(request,Actor(principal),ct)); }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.TapeManageFormatsPolicy);

        api.MapGet("/departments/list", async (bool? includeInactive, IServiceProvider services, CancellationToken ct) =>
        {
            try { return Results.Ok(await Config(services).ListDepartmentsAsync(includeInactive==true,ct)); }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.TapeViewPolicy);

        api.MapPut("/departments/{code}", async (
            string code,
            UpsertTapeDepartmentRequest request,
            ClaimsPrincipal principal,
            IServiceProvider services,
            CancellationToken ct) =>
        {
            if(!string.Equals(code,request.Code,StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new{error="department_code_mismatch",detail="Route and body department codes must match."});
            try { return Results.Ok(await Config(services).UpsertDepartmentAsync(request,Actor(principal),ct)); }
            catch (TapeInventoryRequestException ex) { return Failure(ex); }
        }).RequireAuthorization(MamSecurity.TapeManageDepartmentsPolicy);
    }

    private static ITapeInventoryService Resolve(IServiceProvider services)
    {
        var sql=services.GetService<SqlServerConnectionFactory>();
        var demo=services.GetService<DemoSqliteDatabase>();
        if(sql is null&&demo is null)
            throw new TapeInventoryRequestException("tape_inventory_unavailable","Authoritative tape inventory storage is not configured.",503);
        return new TapeInventoryStore(sql,demo,services.GetRequiredService<IAuditSink>());
    }

    private static ITapeAttachmentService Attachments(IServiceProvider services)
    {
        var sql=services.GetService<SqlServerConnectionFactory>();
        var demo=services.GetService<DemoSqliteDatabase>();
        if(sql is null&&demo is null)
            throw new TapeInventoryRequestException("tape_attachments_unavailable","Authoritative tape attachment storage is not configured.",503);
        return new TapeAttachmentStore(sql,demo,services.GetRequiredService<IAuditSink>());
    }

    private static ITapeManagementConfigurationService Config(IServiceProvider services)
    {
        var sql=services.GetService<SqlServerConnectionFactory>();
        var demo=services.GetService<DemoSqliteDatabase>();
        if(sql is null&&demo is null)
            throw new TapeInventoryRequestException("tape_configuration_unavailable","Authoritative tape configuration storage is not configured.",503);
        return new TapeManagementConfigurationStore(sql,demo,services.GetRequiredService<IAuditSink>());
    }

    private static async Task<IResult?> ValidateDepartmentAsync(string? code,IServiceProvider services,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(code))return null;
        return await Config(services).IsActiveDepartmentAsync(code,ct)
            ? null
            : Results.BadRequest(new{error="invalid_tape_department",detail="Selected tape department is inactive or does not exist."});
    }

    private static Task<bool> Feature(IServiceProvider services,string key,CancellationToken ct) =>
        T22SystemFunctionEndpoints.EnabledAsync(services,key,ct);

    private static IResult Disabled(string key) =>
        Results.Json(new{error="system_function_disabled",functionKey=key,detail="This system function is currently disabled."},statusCode:409);

    private static string Actor(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.WindowsAccountName)
        ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.Identity?.Name
        ?? "unknown";

    private static IResult Failure(TapeInventoryRequestException ex) =>
        Results.Json(new{error=ex.Code,detail=ex.Message,current=ex.Current},statusCode:ex.StatusCode);

    private static IResult Failure(SystemFunctionRequestException ex) =>
        Results.Json(new{error=ex.Code,detail=ex.Message,current=ex.Current},statusCode:ex.StatusCode);
}
