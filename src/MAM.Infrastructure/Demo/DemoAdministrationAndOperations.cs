using System.Text;
using System.Text.Json;
using MAM.Application.Administration;
using MAM.Application.Auditing;
using MAM.Application.Operations;
using MAM.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;

namespace MAM.Infrastructure.Demo;

public sealed class DemoAdministrationService(DemoSqliteDatabase database, IAuditSink audit) : IAdministrationService
{
    public async ValueTask<AdministrationHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        await database.EnsureInitializedAsync(cancellationToken);
        return new AdministrationHealth(true, "SqliteDemo", "Offline demo administration store is ready.");
    }

    public async ValueTask<AdministrationOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        await using var c=await database.OpenAsync(cancellationToken);
        async Task<int> Count(string sql){await using var q=c.CreateCommand();q.CommandText=sql;return Convert.ToInt32(await q.ExecuteScalarAsync(cancellationToken));}
        return new AdministrationOverview(await Count("SELECT COUNT(*) FROM DemoPolicy;"),await Count("SELECT COUNT(*) FROM DemoPolicy WHERE IsEnabled=1;"),await Count("SELECT COUNT(*) FROM DemoUser;"),await Count("SELECT COUNT(*) FROM DemoDictionary;"),await Count("SELECT COUNT(*) FROM DemoPolicy WHERE RequiresRestart=1 AND IsEnabled=1;"),DateTimeOffset.UtcNow);
    }

    public async ValueTask<IReadOnlyList<AdminPolicyRecord>> ListPoliciesAsync(CancellationToken cancellationToken = default)
    {
        await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="SELECT PolicyKey,Category,DisplayNameEn,DisplayNameAr,PayloadJson,SecretRef,Version,RequiresRestart,IsEnabled,UpdatedAtUtc FROM DemoPolicy ORDER BY Category,DisplayNameEn;";await using var r=await q.ExecuteReaderAsync(cancellationToken);var rows=new List<AdminPolicyRecord>();while(await r.ReadAsync(cancellationToken))rows.Add(ReadPolicy(r));return rows;
    }

    public async ValueTask<AdminPolicyRecord?> GetPolicyAsync(string policyKey, CancellationToken cancellationToken = default)=> (await ListPoliciesAsync(cancellationToken)).FirstOrDefault(x=>string.Equals(x.PolicyKey,policyKey,StringComparison.OrdinalIgnoreCase));

    public ValueTask<AdminPolicyValidationResult> ValidatePolicyAsync(string policyKey, AdminPolicyUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var errors=new List<string>();if(string.IsNullOrWhiteSpace(policyKey))errors.Add("Policy key is required.");if(!AdminPolicyCategories.All.Contains(request.Category))errors.Add("Unknown policy category.");if(string.IsNullOrWhiteSpace(request.DisplayNameEn)||string.IsNullOrWhiteSpace(request.DisplayNameAr))errors.Add("English and Arabic display names are required.");return ValueTask.FromResult(new AdminPolicyValidationResult(errors.Count==0,errors,request.RequiresRestart));
    }

    public async ValueTask<AdminPolicyRecord> UpsertPolicyAsync(string policyKey, AdminPolicyUpdateRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        var validation=await ValidatePolicyAsync(policyKey,request,cancellationToken);if(!validation.Valid)throw new AdministrationRequestException("policy_invalid",string.Join(" ",validation.Errors),400);var current=await GetPolicyAsync(policyKey,cancellationToken);if(current is not null&&current.Version!=request.ExpectedVersion)throw new AdministrationRequestException("version_conflict","Policy changed. Refresh and retry.",409,current);if(current is null&&request.ExpectedVersion!=0)throw new AdministrationRequestException("version_conflict","New policies require ExpectedVersion=0.",409);
        var now=DateTimeOffset.UtcNow;var version=(current?.Version??0)+1;await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="INSERT INTO DemoPolicy(PolicyKey,Category,DisplayNameEn,DisplayNameAr,PayloadJson,SecretRef,Version,RequiresRestart,IsEnabled,UpdatedAtUtc) VALUES($key,$category,$en,$ar,$payload,$secret,$version,$restart,$enabled,$now) ON CONFLICT(PolicyKey) DO UPDATE SET Category=excluded.Category,DisplayNameEn=excluded.DisplayNameEn,DisplayNameAr=excluded.DisplayNameAr,PayloadJson=excluded.PayloadJson,SecretRef=excluded.SecretRef,Version=excluded.Version,RequiresRestart=excluded.RequiresRestart,IsEnabled=excluded.IsEnabled,UpdatedAtUtc=excluded.UpdatedAtUtc;";q.Parameters.AddWithValue("$key",policyKey.Trim());q.Parameters.AddWithValue("$category",request.Category);q.Parameters.AddWithValue("$en",request.DisplayNameEn.Trim());q.Parameters.AddWithValue("$ar",request.DisplayNameAr.Trim());q.Parameters.AddWithValue("$payload",request.Payload.GetRawText());q.Parameters.AddWithValue("$secret",Db(request.SecretRef));q.Parameters.AddWithValue("$version",version);q.Parameters.AddWithValue("$restart",request.RequiresRestart?1:0);q.Parameters.AddWithValue("$enabled",request.IsEnabled?1:0);q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(now));await q.ExecuteNonQueryAsync(cancellationToken);await Audit(actorId,"administration.policy.updated","Policy",policyKey,cancellationToken);return (await GetPolicyAsync(policyKey,cancellationToken))!;
    }

    public async ValueTask<AdminConnectionTestResult> TestPolicyAsync(string policyKey, string actorId, CancellationToken cancellationToken = default)
    {
        var policy=await GetPolicyAsync(policyKey,cancellationToken);if(policy is null)throw new AdministrationRequestException("policy_not_found","Policy was not found.",404);await Audit(actorId,"administration.policy.tested","Policy",policyKey,cancellationToken);return new AdminConnectionTestResult(true,"demo_local_ready","Offline demo policy is locally available.","DEMO-LOCAL",!string.IsNullOrWhiteSpace(policy.SecretRef),false);
    }

    public async ValueTask<IReadOnlyList<AdminUserPolicyRecord>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="SELECT UserId,UserName,DisplayName,ExternalSubject,IsEnabled,Version,RolesJson FROM DemoUser ORDER BY UserName;";await using var r=await q.ExecuteReaderAsync(cancellationToken);var rows=new List<AdminUserPolicyRecord>();while(await r.ReadAsync(cancellationToken))rows.Add(new AdminUserPolicyRecord(Guid.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.GetInt32(4)!=0,r.GetInt64(5),JsonSerializer.Deserialize<string[]>(r.GetString(6))??[]));return rows;
    }

    public async ValueTask<AdminUserPolicyRecord> UpsertUserAsync(Guid userId, AdminUserPolicyUpdateRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        if(string.IsNullOrWhiteSpace(request.UserName)||string.IsNullOrWhiteSpace(request.DisplayName))throw new AdministrationRequestException("user_invalid","User name and display name are required.",400);var current=(await ListUsersAsync(cancellationToken)).FirstOrDefault(x=>x.UserId==userId);if(current is not null&&current.Version!=request.ExpectedVersion)throw new AdministrationRequestException("version_conflict","User changed. Refresh and retry.",409,current);if(current is null&&request.ExpectedVersion!=0)throw new AdministrationRequestException("version_conflict","New users require ExpectedVersion=0.",409);var version=(current?.Version??0)+1;var roles=(request.Roles??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="INSERT INTO DemoUser(UserId,UserName,DisplayName,ExternalSubject,IsEnabled,Version,RolesJson) VALUES($id,$name,$display,$subject,$enabled,$version,$roles) ON CONFLICT(UserId) DO UPDATE SET UserName=excluded.UserName,DisplayName=excluded.DisplayName,ExternalSubject=excluded.ExternalSubject,IsEnabled=excluded.IsEnabled,Version=excluded.Version,RolesJson=excluded.RolesJson;";q.Parameters.AddWithValue("$id",userId.ToString("D"));q.Parameters.AddWithValue("$name",request.UserName.Trim());q.Parameters.AddWithValue("$display",request.DisplayName.Trim());q.Parameters.AddWithValue("$subject",Db(request.ExternalSubject));q.Parameters.AddWithValue("$enabled",request.IsEnabled?1:0);q.Parameters.AddWithValue("$version",version);q.Parameters.AddWithValue("$roles",JsonSerializer.Serialize(roles));try{await q.ExecuteNonQueryAsync(cancellationToken);}catch(SqliteException ex)when(ex.SqliteErrorCode==19){throw new AdministrationRequestException("user_name_conflict","User name already exists.",409);}await Audit(actorId,"administration.user.updated","MamUser",userId.ToString("D"),cancellationToken);return (await ListUsersAsync(cancellationToken)).First(x=>x.UserId==userId);
    }

    public async ValueTask<IReadOnlyList<AdminDictionaryEntry>> ListDictionaryAsync(string dictionaryKey, CancellationToken cancellationToken = default)
    {
        await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="SELECT DictionaryKey,EntryKey,LabelEn,LabelAr,IsEnabled,Version,UpdatedAtUtc FROM DemoDictionary WHERE DictionaryKey=$key ORDER BY EntryKey;";q.Parameters.AddWithValue("$key",dictionaryKey.Trim());await using var r=await q.ExecuteReaderAsync(cancellationToken);var rows=new List<AdminDictionaryEntry>();while(await r.ReadAsync(cancellationToken))rows.Add(new AdminDictionaryEntry(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt32(4)!=0,r.GetInt64(5),DemoSqliteDatabase.FromDb(r.GetString(6))));return rows;
    }

    public async ValueTask<AdminDictionaryEntry> UpsertDictionaryEntryAsync(string dictionaryKey, string entryKey, AdminDictionaryUpdateRequest request, string actorId, CancellationToken cancellationToken = default)
    {
        if(string.IsNullOrWhiteSpace(dictionaryKey)||string.IsNullOrWhiteSpace(entryKey)||string.IsNullOrWhiteSpace(request.LabelEn)||string.IsNullOrWhiteSpace(request.LabelAr))throw new AdministrationRequestException("dictionary_invalid","Dictionary key, entry key and both labels are required.",400);var current=(await ListDictionaryAsync(dictionaryKey,cancellationToken)).FirstOrDefault(x=>string.Equals(x.EntryKey,entryKey,StringComparison.OrdinalIgnoreCase));if(current is not null&&current.Version!=request.ExpectedVersion)throw new AdministrationRequestException("version_conflict","Dictionary entry changed. Refresh and retry.",409,current);if(current is null&&request.ExpectedVersion!=0)throw new AdministrationRequestException("version_conflict","New dictionary entries require ExpectedVersion=0.",409);var now=DateTimeOffset.UtcNow;var version=(current?.Version??0)+1;await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="INSERT INTO DemoDictionary(DictionaryKey,EntryKey,LabelEn,LabelAr,IsEnabled,Version,UpdatedAtUtc) VALUES($dictionary,$entry,$en,$ar,$enabled,$version,$now) ON CONFLICT(DictionaryKey,EntryKey) DO UPDATE SET LabelEn=excluded.LabelEn,LabelAr=excluded.LabelAr,IsEnabled=excluded.IsEnabled,Version=excluded.Version,UpdatedAtUtc=excluded.UpdatedAtUtc;";q.Parameters.AddWithValue("$dictionary",dictionaryKey.Trim());q.Parameters.AddWithValue("$entry",entryKey.Trim());q.Parameters.AddWithValue("$en",request.LabelEn.Trim());q.Parameters.AddWithValue("$ar",request.LabelAr.Trim());q.Parameters.AddWithValue("$enabled",request.IsEnabled?1:0);q.Parameters.AddWithValue("$version",version);q.Parameters.AddWithValue("$now",DemoSqliteDatabase.ToDb(now));await q.ExecuteNonQueryAsync(cancellationToken);await Audit(actorId,"administration.dictionary.updated","Dictionary",dictionaryKey+":"+entryKey,cancellationToken);return (await ListDictionaryAsync(dictionaryKey,cancellationToken)).First(x=>string.Equals(x.EntryKey,entryKey,StringComparison.OrdinalIgnoreCase));
    }

    public async ValueTask<AdminAuditResult> QueryAuditAsync(AdminAuditQuery query, CancellationToken cancellationToken = default)
    {
        var rows=(await audit.ListRecentAsync(Math.Clamp(query.Limit,1,5000),cancellationToken)).Where(x=>string.IsNullOrWhiteSpace(query.Actor)||x.ActorId.Contains(query.Actor,StringComparison.OrdinalIgnoreCase)).Where(x=>string.IsNullOrWhiteSpace(query.Action)||x.Action.Contains(query.Action,StringComparison.OrdinalIgnoreCase)).Where(x=>string.IsNullOrWhiteSpace(query.Outcome)||string.Equals(x.Outcome,query.Outcome,StringComparison.OrdinalIgnoreCase)).Where(x=>query.FromUtc is null||x.OccurredAtUtc>=query.FromUtc).Where(x=>query.ToUtc is null||x.OccurredAtUtc<=query.ToUtc).Take(Math.Clamp(query.Limit,1,5000)).ToArray();return new AdminAuditResult(rows,Math.Clamp(query.Limit,1,5000),DateTimeOffset.UtcNow);
    }

    public async ValueTask<string> ExportAuditCsvAsync(AdminAuditQuery query, CancellationToken cancellationToken = default)
    {
        var result=await QueryAuditAsync(query with { Limit=Math.Clamp(query.Limit,1,5000)},cancellationToken);var sb=new StringBuilder("OccurredAtUtc,ActorId,Action,EntityType,EntityId,Outcome,Detail\r\n");foreach(var x in result.Items)sb.AppendLine(string.Join(',',Csv(DemoSqliteDatabase.ToDb(x.OccurredAtUtc)),Csv(x.ActorId),Csv(x.Action),Csv(x.EntityType),Csv(x.EntityId),Csv(x.Outcome),Csv(x.Detail??"")));return sb.ToString();
    }

    private async Task Audit(string actor,string action,string type,string id,CancellationToken ct)=>await audit.AppendAsync(new AuditEvent(Guid.NewGuid(),DateTimeOffset.UtcNow,actor,action,type,id,"Success","provider=SqliteDemo"),ct);
    private static AdminPolicyRecord ReadPolicy(SqliteDataReader r){using var doc=JsonDocument.Parse(r.GetString(4));return new AdminPolicyRecord(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),doc.RootElement.Clone(),r.IsDBNull(5)?null:r.GetString(5),r.GetInt64(6),r.GetInt32(7)!=0,r.GetInt32(8)!=0,DemoSqliteDatabase.FromDb(r.GetString(9)));}
    private static object Db(string? v)=>string.IsNullOrWhiteSpace(v)?DBNull.Value:v.Trim();
    private static string Csv(string v)=>"\""+v.Replace("\"","\"\"")+"\"";
}

public sealed class DemoOperationsService(DemoSqliteDatabase database, MamSettings settings) : IOperationsService
{
    public async ValueTask<OperationsHealth> GetHealthAsync(CancellationToken cancellationToken = default){await database.EnsureInitializedAsync(cancellationToken);return new OperationsHealth(true,"SqliteDemo","Offline demo operational reporting is ready.");}

    public async Task<OperationalSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        await using var c=await database.OpenAsync(cancellationToken);async Task<long> C(string sql){await using var q=c.CreateCommand();q.CommandText=sql;return Convert.ToInt64(await q.ExecuteScalarAsync(cancellationToken)??0);}return new OperationalSummary(await C("SELECT COUNT(*) FROM DemoAsset;"),await C("SELECT COUNT(*) FROM DemoAsset WHERE OriginalObjectKey IS NOT NULL;"),await C("SELECT COALESCE(SUM(OriginalLength),0) FROM DemoAsset;"),await C("SELECT COUNT(*) FROM DemoUploadSession WHERE State=0;"),await C("SELECT COUNT(*) FROM DemoUploadSession WHERE State=2;"),await C("SELECT COUNT(*) FROM DemoProcessingJob WHERE State=0;"),await C("SELECT COUNT(*) FROM DemoProcessingJob WHERE State=1;"),await C("SELECT COUNT(*) FROM DemoProcessingJob WHERE State=3;"),0,0,await C("SELECT COUNT(*) FROM DemoProtection WHERE State=2;"),await C("SELECT COUNT(*) FROM DemoProtection WHERE State=1;"),await C("SELECT COUNT(*) FROM DemoProtection WHERE State=0;"),await C("SELECT COUNT(*) FROM DemoProtection WHERE State=2;"),await C("SELECT COUNT(*) FROM DemoProtection WHERE State=3;"),DateTimeOffset.UtcNow);
    }

    public async Task<IngestThroughputReport> GetIngestThroughputAsync(int windowHours, CancellationToken cancellationToken = default)
    {
        var hours=Math.Clamp(windowHours,1,24*365);var from=DateTimeOffset.UtcNow.AddHours(-hours);await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="SELECT COUNT(*),COALESCE(SUM(ExpectedLength),0) FROM DemoUploadSession WHERE State IN (1,3,4) AND CreatedAtUtc >= $from;";q.Parameters.AddWithValue("$from",DemoSqliteDatabase.ToDb(from));await using var r=await q.ExecuteReaderAsync(cancellationToken);await r.ReadAsync(cancellationToken);var sessions=r.GetInt64(0);var bytes=r.GetInt64(1);return new IngestThroughputReport(hours,from,DateTimeOffset.UtcNow,sessions,bytes,bytes/(hours*3600d));
    }

    public async Task<DurableQueueReport> GetQueuesAsync(CancellationToken cancellationToken = default)
    {
        var s=await GetSummaryAsync(cancellationToken);return new DurableQueueReport([new DurableQueueState("Upload",s.UploadReceiving,0,s.UploadFailed,0,null),new DurableQueueState("Processing",s.ProcessingQueued,s.ProcessingLeased,s.ProcessingFailed,0,null),new DurableQueueState("Backup",s.BackupQueued,s.BackupLeased,s.BackupFailed,0,null)],DateTimeOffset.UtcNow);
    }

    public async Task<IntegrityProtectionReport> GetIntegrityAsync(CancellationToken cancellationToken = default)
    {
        var s=await GetSummaryAsync(cancellationToken);await using var c=await database.OpenAsync(cancellationToken);await using var q=c.CreateCommand();q.CommandText="SELECT COALESCE(SUM(OriginalLength),0),COALESCE(SUM(CASE WHEN p.State=1 THEN a.OriginalLength ELSE 0 END),0),MIN(p.LastIntegrityCheckAtUtc) FROM DemoAsset a LEFT JOIN DemoProtection p ON p.AssetId=a.AssetId WHERE a.OriginalObjectKey IS NOT NULL;";await using var r=await q.ExecuteReaderAsync(cancellationToken);await r.ReadAsync(cancellationToken);return new IntegrityProtectionReport(s.Originals,r.GetInt64(0),s.Protected,r.GetInt64(1),s.ProtectionPending,s.ProtectionFailed,s.ProtectionMismatch,r.IsDBNull(2)?null:DemoSqliteDatabase.FromDb(r.GetString(2)),DateTimeOffset.UtcNow);
    }

    public async Task<StorageUsageReport> GetStorageUsageAsync(CancellationToken cancellationToken = default)
    {
        var integrity=await GetIntegrityAsync(cancellationToken);return new StorageUsageReport(settings.Storage.Primary.Id,settings.Storage.Backup.Id,integrity.OriginalBytes,integrity.ProtectedBytes,integrity.Originals,integrity.Protected,DateTimeOffset.UtcNow);
    }
}
