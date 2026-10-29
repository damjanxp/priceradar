-- Row shape MatchJob and ResolveJob send to ingest.usp_ApplyMatchResults (spec §5.3).
IF TYPE_ID(N'ingest.MatchResultType') IS NULL
    CREATE TYPE ingest.MatchResultType AS TABLE
    (
        StagingListingId   bigint        NOT NULL PRIMARY KEY,
        PriceRsd           decimal(12,2) NULL,
        NormTitle          varchar(300)  NULL,
        NormAuthor         varchar(200)  NULL,
        BookId             int           NULL,  -- existing book; NULL for NewBook / Pending / rejected rows
        MatchStatus        tinyint       NULL,  -- 0 Pending, 1 AutoMatched, 2 AiMatched, 3 NewBook, 4 Manual
        MatchScore         decimal(5,4)  NULL,
        ProcessStatus      tinyint       NOT NULL,  -- 1 Normalized, 3 Rejected
        RejectReason       varchar(50)   NULL,
        NewBookTitle       nvarchar(300) NULL,  -- display form, only for MatchStatus = 3
        NewBookAuthor      nvarchar(200) NULL,
        NewBookOrigin      tinyint       NULL,  -- 1 Rule, 2 Ai
        NewBookNeedsReview bit           NULL
    );
