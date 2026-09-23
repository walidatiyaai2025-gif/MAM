SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.MamMessageLibrary', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MamMessageLibrary
    (
        MessageKey nvarchar(160) NOT NULL CONSTRAINT PK_MamMessageLibrary PRIMARY KEY,
        Scope nvarchar(80) NOT NULL,
        MatchPattern nvarchar(600) NOT NULL,
        TitleEn nvarchar(240) NOT NULL,
        TitleAr nvarchar(240) NOT NULL,
        MessageEn nvarchar(1200) NOT NULL,
        MessageAr nvarchar(1200) NOT NULL,
        ReasonEn nvarchar(1000) NULL,
        ReasonAr nvarchar(1000) NULL,
        Severity nvarchar(20) NOT NULL CONSTRAINT DF_MamMessageLibrary_Severity DEFAULT (N'error'),
        ShowRetry bit NOT NULL CONSTRAINT DF_MamMessageLibrary_ShowRetry DEFAULT (0),
        IsEnabled bit NOT NULL CONSTRAINT DF_MamMessageLibrary_IsEnabled DEFAULT (1),
        SortOrder int NOT NULL CONSTRAINT DF_MamMessageLibrary_SortOrder DEFAULT (100),
        Version bigint NOT NULL CONSTRAINT DF_MamMessageLibrary_Version DEFAULT (1),
        UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_MamMessageLibrary_UpdatedAtUtc DEFAULT SYSUTCDATETIME(),
        RowVersion rowversion NOT NULL,
        CONSTRAINT CK_MamMessageLibrary_Severity CHECK (Severity IN (N'info',N'success',N'warning',N'error')),
        CONSTRAINT CK_MamMessageLibrary_Version CHECK (Version > 0),
        CONSTRAINT CK_MamMessageLibrary_SortOrder CHECK (SortOrder BETWEEN 0 AND 100000)
    );
    CREATE INDEX IX_MamMessageLibrary_ScopeEnabled
        ON dbo.MamMessageLibrary(Scope, IsEnabled, SortOrder, MessageKey);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamMessageLibrary WHERE MessageKey=N'processing.ocr.no-text')
BEGIN
    INSERT dbo.MamMessageLibrary
    (MessageKey,Scope,MatchPattern,TitleEn,TitleAr,MessageEn,MessageAr,ReasonEn,ReasonAr,Severity,ShowRetry,IsEnabled,SortOrder)
    VALUES
    (N'processing.ocr.no-text',N'processing',N'OCR did not detect readable text',
     N'OCR text extraction',N'استخراج النص OCR',
     N'No readable text was detected in this file.',N'لم يتم العثور على نص قابل للاستخراج في هذا الملف.',
     N'OCR did not recognize any readable text in the source.',N'لم يتعرف OCR على أي نص مقروء في الملف.',
     N'warning',0,1,10);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamMessageLibrary WHERE MessageKey=N'processing.ocr.zero-length-legacy')
BEGIN
    INSERT dbo.MamMessageLibrary
    (MessageKey,Scope,MatchPattern,TitleEn,TitleAr,MessageEn,MessageAr,ReasonEn,ReasonAr,Severity,ShowRetry,IsEnabled,SortOrder)
    VALUES
    (N'processing.ocr.zero-length-legacy',N'processing',N'CK_MamMediaDerivative_Length',
     N'OCR text extraction',N'استخراج النص OCR',
     N'No readable text was detected in this file.',N'لم يتم العثور على نص قابل للاستخراج في هذا الملف.',
     N'The OCR result was empty, so no text derivative was saved.',N'نتيجة OCR كانت فارغة، لذلك لم يتم حفظ مشتق نصي.',
     N'warning',0,1,11);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamMessageLibrary WHERE MessageKey=N'processing.derivative.already-exists')
BEGIN
    INSERT dbo.MamMessageLibrary
    (MessageKey,Scope,MatchPattern,TitleEn,TitleAr,MessageEn,MessageAr,ReasonEn,ReasonAr,Severity,ShowRetry,IsEnabled,SortOrder)
    VALUES
    (N'processing.derivative.already-exists',N'processing',N'already exists',
     N'Result already exists',N'النتيجة موجودة بالفعل',
     N'This processing result already exists.',N'هذه النتيجة موجودة بالفعل.',
     N'The same result was created previously.',N'تم إنشاء نفس النتيجة مسبقًا.',
     N'info',0,1,20);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamMessageLibrary WHERE MessageKey=N'processing.derivative.duplicate-key')
BEGIN
    INSERT dbo.MamMessageLibrary
    (MessageKey,Scope,MatchPattern,TitleEn,TitleAr,MessageEn,MessageAr,ReasonEn,ReasonAr,Severity,ShowRetry,IsEnabled,SortOrder)
    VALUES
    (N'processing.derivative.duplicate-key',N'processing',N'duplicate key',
     N'Result already exists',N'النتيجة موجودة بالفعل',
     N'This processing result already exists.',N'هذه النتيجة موجودة بالفعل.',
     N'The same result was created previously.',N'تم إنشاء نفس النتيجة مسبقًا.',
     N'info',0,1,21);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamMessageLibrary WHERE MessageKey=N'processing.ocr.tool-unavailable')
BEGIN
    INSERT dbo.MamMessageLibrary
    (MessageKey,Scope,MatchPattern,TitleEn,TitleAr,MessageEn,MessageAr,ReasonEn,ReasonAr,Severity,ShowRetry,IsEnabled,SortOrder)
    VALUES
    (N'processing.ocr.tool-unavailable',N'processing',N'Required OCR processing tool',
     N'OCR text extraction',N'استخراج النص OCR',
     N'OCR is temporarily unavailable.',N'خدمة استخراج النص OCR غير متاحة مؤقتًا.',
     N'The OCR processing component is not available on the server.',N'مكوّن معالجة OCR غير متاح على الخادم حاليًا.',
     N'error',1,1,30);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamMessageLibrary WHERE MessageKey=N'processing.primary-original-not-found')
BEGIN
    INSERT dbo.MamMessageLibrary
    (MessageKey,Scope,MatchPattern,TitleEn,TitleAr,MessageEn,MessageAr,ReasonEn,ReasonAr,Severity,ShowRetry,IsEnabled,SortOrder)
    VALUES
    (N'processing.primary-original-not-found',N'processing',N'Primary original is unavailable',
     N'Processing could not start',N'تعذر بدء المعالجة',
     N'The original media file is currently unavailable.',N'ملف الميديا الأصلي غير متاح حاليًا.',
     N'The processing service could not access the authoritative original.',N'خدمة المعالجة لم تتمكن من الوصول إلى الملف الأصلي الموثوق.',
     N'error',1,1,40);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.MamSchemaVersion WHERE MigrationId=N'0023_message_library')
    INSERT dbo.MamSchemaVersion(MigrationId) VALUES(N'0023_message_library');

COMMIT TRANSACTION;
