-- "One row is one version of one source, as ingested under one embedding model."
-- Text that is compared uses a binary collation, so it matches by exact value as the contract requires.

CREATE TABLE [knowledge].[SourceVersion] (
    [Id]               char(32)       COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SourceId]         varchar(128)   COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Origin]           nvarchar(2048) NOT NULL,
    [Version]          varchar(128)   COLLATE Latin1_General_100_BIN2 NOT NULL,
    [EmbeddingModelId] nvarchar(200)  COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Dimensions]       int            NOT NULL,
    [ChunkCount]       int            NOT NULL,
    [CreatedAt]        datetimeoffset(7) NOT NULL,
    [SupersededAt]     datetimeoffset(7) NULL,
    CONSTRAINT [PK_SourceVersion] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [CK_SourceVersion_Id_Format] CHECK ([Id] NOT LIKE '%[^0-9a-f]%'),
    CONSTRAINT [CK_SourceVersion_SourceId_Format] CHECK ([SourceId] LIKE '[a-z0-9]%' AND [SourceId] NOT LIKE '%[^a-z0-9._:-]%'),
    CONSTRAINT [CK_SourceVersion_Origin_Length] CHECK (DATALENGTH([Origin]) >= 2),
    CONSTRAINT [CK_SourceVersion_Dimensions] CHECK ([Dimensions] BETWEEN 1 AND 8192),
    CONSTRAINT [CK_SourceVersion_ChunkCount] CHECK ([ChunkCount] BETWEEN 1 AND 1024),
    -- A superseding host's clock can run behind the clock that stored this version; the provider then holds the
    -- superseded time at the creation time, so the lineage never runs backwards.
    CONSTRAINT [CK_SourceVersion_SupersededAt] CHECK ([SupersededAt] IS NULL OR [SupersededAt] >= [CreatedAt])
);
GO

-- Rule: a source has at most one current version. Serves reading a source's current version, the search's
-- current versions of the named sources, and the check a store makes before it writes.
CREATE UNIQUE INDEX [UQ_SourceVersion_SourceId_Current] ON [knowledge].[SourceVersion] ([SourceId]) WHERE [SupersededAt] IS NULL;
GO

-- Serves deleting a source with every version, superseded ones included.
CREATE INDEX [IX_SourceVersion_SourceId] ON [knowledge].[SourceVersion] ([SourceId]);
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'One version of one source, as ingested under one embedding model. Insert, then SupersededAt set once when a newer version replaces it; the rows of one source are its lineage, so there is no other history. Growth: one row per new version; rows leave only when the host deletes the source. The store cannot know what a host ingests. Classification: Confidential.',
    @level0type = N'SCHEMA', @level0name = N'knowledge', @level1type = N'TABLE', @level1name = N'SourceVersion';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'The version id: a version 7 GUID as 32 lower-case hexadecimal digits, assigned by the provider from the ingest instant.',
    @level0type = N'SCHEMA', @level0name = N'knowledge', @level1type = N'TABLE', @level1name = N'SourceVersion', @level2type = N'COLUMN', @level2name = N'Id';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'The host''s id for the source: 1 to 128 characters of a-z, 0-9, ''.'', ''_'', '':'' or ''-'', starting with a-z or 0-9. Sources are not rows of their own: a source is the set of its versions.',
    @level0type = N'SCHEMA', @level0name = N'knowledge', @level1type = N'TABLE', @level1name = N'SourceVersion', @level2type = N'COLUMN', @level2name = N'SourceId';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'Where the host says the source comes from, for example an address: 1 to 2,048 characters with no control character.',
    @level0type = N'SCHEMA', @level0name = N'knowledge', @level1type = N'TABLE', @level1name = N'SourceVersion', @level2type = N'COLUMN', @level2name = N'Origin';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'The host''s version string for this ingest of the source: 1 to 128 printable ASCII characters with no space, compared by exact value.',
    @level0type = N'SCHEMA', @level0name = N'knowledge', @level1type = N'TABLE', @level1name = N'SourceVersion', @level2type = N'COLUMN', @level2name = N'Version';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'The id of the embedding model that made every vector of this version, as the model reports it: 1 to 200 characters, compared by exact value. Models are not rows in this database; a search scores only versions of the query''s model.',
    @level0type = N'SCHEMA', @level0name = N'knowledge', @level1type = N'TABLE', @level1name = N'SourceVersion', @level2type = N'COLUMN', @level2name = N'EmbeddingModelId';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'The number of single-precision values in every vector of this version, 1 to 8,192. A search scores only versions whose length equals the query''s.',
    @level0type = N'SCHEMA', @level0name = N'knowledge', @level1type = N'TABLE', @level1name = N'SourceVersion', @level2type = N'COLUMN', @level2name = N'Dimensions';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'The number of chunks the version was stored with, 1 to 1,024. Kept after the version is superseded and its chunks are deleted.',
    @level0type = N'SCHEMA', @level0name = N'knowledge', @level1type = N'TABLE', @level1name = N'SourceVersion', @level2type = N'COLUMN', @level2name = N'ChunkCount';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'The ingest instant, in UTC, by the ingesting host''s clock: the contract''s LastUpdatedAt. A version is written once, so this is also the row''s creation stamp.',
    @level0type = N'SCHEMA', @level0name = N'knowledge', @level1type = N'TABLE', @level1name = N'SourceVersion', @level2type = N'COLUMN', @level2name = N'CreatedAt';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'The instant a newer version replaced this one, in UTC: the newer version''s ingest instant, or this row''s CreatedAt when that is later. Null means this is the source''s current version. Set once.',
    @level0type = N'SCHEMA', @level0name = N'knowledge', @level1type = N'TABLE', @level1name = N'SourceVersion', @level2type = N'COLUMN', @level2name = N'SupersededAt';
GO
