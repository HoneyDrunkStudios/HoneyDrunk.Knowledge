-- "One row is one retrievable passage of one version, with its embedding."
-- The rows are parts of one version and are only read through it, so the key is the version and the position.
-- The primary key also serves the foreign key and every chunk read, so the table has no other index.

CREATE TABLE [knowledge].[Chunk] (
    [SourceVersionId] char(32)       COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Position]        int            NOT NULL,
    [Text]            nvarchar(1200) NOT NULL,
    [Vector]          varbinary(max) NOT NULL,
    [CreatedAt]       datetimeoffset(7) NOT NULL,
    CONSTRAINT [PK_Chunk] PRIMARY KEY CLUSTERED ([SourceVersionId], [Position]),
    CONSTRAINT [FK_Chunk_SourceVersion] FOREIGN KEY ([SourceVersionId]) REFERENCES [knowledge].[SourceVersion] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [CK_Chunk_Position] CHECK ([Position] BETWEEN 0 AND 1023),
    CONSTRAINT [CK_Chunk_Text_Length] CHECK (DATALENGTH([Text]) >= 2),
    CONSTRAINT [CK_Chunk_Vector_Length] CHECK (DATALENGTH([Vector]) BETWEEN 4 AND 32768 AND DATALENGTH([Vector]) % 4 = 0)
);
GO

EXECUTE sp_addextendedproperty @name = N'MS_Description',
    @value = N'One retrievable passage of one source version, with its embedding. Insert only; deleted in the same transaction as its version is superseded, and with its version when the source is deleted, so there is no row history and old text cannot be recovered. Growth: at most 1,024 rows per current version and none for superseded versions. The store cannot know what a host ingests. Classification: Confidential.',
    @level0type = N'SCHEMA', @level0name = N'knowledge', @level1type = N'TABLE', @level1name = N'Chunk';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'The version the passage belongs to.',
    @level0type = N'SCHEMA', @level0name = N'knowledge', @level1type = N'TABLE', @level1name = N'Chunk', @level2type = N'COLUMN', @level2name = N'SourceVersionId';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'The passage''s position in its version, from 0 to 1,023; a version''s chunks hold positions 0 to ChunkCount - 1.',
    @level0type = N'SCHEMA', @level0name = N'knowledge', @level1type = N'TABLE', @level1name = N'Chunk', @level2type = N'COLUMN', @level2name = N'Position';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'The passage text, verbatim: 1 to 1,200 UTF-16 code units. Never written to logs or telemetry.',
    @level0type = N'SCHEMA', @level0name = N'knowledge', @level1type = N'TABLE', @level1name = N'Chunk', @level2type = N'COLUMN', @level2name = N'Text';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'The passage''s embedding: the version''s Dimensions values as little-endian IEEE 754 single-precision floats, packed, 4 to 32,768 bytes. Read back bit for bit; never written to logs or telemetry.',
    @level0type = N'SCHEMA', @level0name = N'knowledge', @level1type = N'TABLE', @level1name = N'Chunk', @level2type = N'COLUMN', @level2name = N'Vector';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'The instant the row was written, in UTC: the ingest instant of its version, which is written in the same transaction.',
    @level0type = N'SCHEMA', @level0name = N'knowledge', @level1type = N'TABLE', @level1name = N'Chunk', @level2type = N'COLUMN', @level2name = N'CreatedAt';
GO
