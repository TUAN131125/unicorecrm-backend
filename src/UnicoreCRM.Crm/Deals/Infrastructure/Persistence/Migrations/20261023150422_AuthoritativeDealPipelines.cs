using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Deals.Infrastructure.Persistence.Migrations;

public partial class AuthoritativeDealPipelines : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM deals.Deals WHERE
                StageCode NOT IN ('DISCOVERY','QUALIFIED','SOLUTION','PROPOSAL','NEGOTIATION','WON','LOST')
                OR StageCategory <> CASE StageCode WHEN 'WON' THEN 'Won' WHEN 'LOST' THEN 'Lost' ELSE 'Open' END)
                THROW 51002, 'Unknown legacy Deal stage; pipeline backfill refused', 1;
            ALTER TABLE deals.Deals ADD PipelineId nvarchar(128) NULL;
            CREATE TABLE deals.Pipelines (
                WorkspaceId nvarchar(128) NOT NULL, PipelineId nvarchar(128) NOT NULL,
                NameVi nvarchar(200) NOT NULL, NameEn nvarchar(200) NOT NULL,
                Description nvarchar(2000) NOT NULL, IsDefault bit NOT NULL, IsActive bit NOT NULL,
                Version bigint NOT NULL, CreatedAt datetimeoffset NOT NULL, UpdatedAt datetimeoffset NOT NULL,
                UpdatedBy nvarchar(128) NOT NULL,
                CONSTRAINT PK_Pipelines PRIMARY KEY (WorkspaceId,PipelineId),
                CONSTRAINT CK_Pipelines_DefaultActive CHECK (IsDefault=0 OR IsActive=1)
            );
            CREATE UNIQUE INDEX IX_Pipelines_WorkspaceId ON deals.Pipelines(WorkspaceId) WHERE IsDefault=1 AND IsActive=1;
            CREATE TABLE deals.PipelineStages (
                WorkspaceId nvarchar(128) NOT NULL, PipelineId nvarchar(128) NOT NULL, StageId nvarchar(128) NOT NULL,
                Code nvarchar(120) NOT NULL, LabelVi nvarchar(200) NOT NULL, LabelEn nvarchar(200) NOT NULL,
                [Order] int NOT NULL, Category nvarchar(16) NOT NULL, DefaultProbability int NOT NULL,
                Color nvarchar(16) NOT NULL, IsActive bit NOT NULL, IsSystem bit NOT NULL,
                ForecastFloor nvarchar(24) NOT NULL, RequiresPositiveAmount bit NOT NULL,
                CONSTRAINT PK_PipelineStages PRIMARY KEY (WorkspaceId,PipelineId,StageId),
                CONSTRAINT FK_PipelineStages_Pipelines_WorkspaceId_PipelineId FOREIGN KEY (WorkspaceId,PipelineId)
                    REFERENCES deals.Pipelines(WorkspaceId,PipelineId) ON DELETE CASCADE,
                CONSTRAINT CK_PipelineStages_Probability CHECK (DefaultProbability BETWEEN 0 AND 100),
                CONSTRAINT CK_PipelineStages_Category CHECK (Category IN ('Open','Won','Lost'))
            );
            CREATE UNIQUE INDEX IX_PipelineStages_WorkspaceId_PipelineId_Code ON deals.PipelineStages(WorkspaceId,PipelineId,Code);
            INSERT INTO deals.Pipelines (WorkspaceId,PipelineId,NameVi,NameEn,Description,IsDefault,IsActive,Version,CreatedAt,UpdatedAt,UpdatedBy)
            SELECT DISTINCT WorkspaceId,'dpl_'+LOWER(LEFT(CONVERT(varchar(64),HASHBYTES('SHA2_256',WorkspaceId),2),32)),
                N'Quy trình bán hàng',N'Sales pipeline',N'',1,1,1,SYSUTCDATETIME(),SYSUTCDATETIME(),'SCHEMA_MIGRATION'
            FROM deals.Deals;
            INSERT INTO deals.PipelineStages (WorkspaceId,PipelineId,StageId,Code,LabelVi,LabelEn,[Order],Category,DefaultProbability,Color,IsActive,IsSystem,ForecastFloor,RequiresPositiveAmount)
            SELECT p.WorkspaceId,p.PipelineId,'dst_'+v.Code,v.Code,v.LabelVi,v.LabelEn,v.[Order],v.Category,v.Probability,v.Color,1,1,v.ForecastFloor,v.Positive
            FROM deals.Pipelines p CROSS JOIN (VALUES
                ('DISCOVERY',N'Khám phá',N'Discovery',10,'Open',10,'blue','Pipeline',0),
                ('QUALIFIED',N'Đã đánh giá',N'Qualified',20,'Open',30,'teal','Pipeline',0),
                ('SOLUTION',N'Giải pháp',N'Solution',30,'Open',50,'purple','BestCase',0),
                ('PROPOSAL',N'Đề xuất',N'Proposal',40,'Open',65,'yellow','BestCase',1),
                ('NEGOTIATION',N'Đàm phán',N'Negotiation',50,'Open',80,'orange','Commit',1),
                ('WON',N'Thắng',N'Won',60,'Won',100,'green','Pipeline',0),
                ('LOST',N'Thất bại',N'Lost',70,'Lost',0,'red','Pipeline',0)
            ) v(Code,LabelVi,LabelEn,[Order],Category,Probability,Color,ForecastFloor,Positive);
            """);
        // A separate command compiles after the new column exists.
        migrationBuilder.Sql("""
            UPDATE d SET PipelineId=p.PipelineId FROM deals.Deals d JOIN deals.Pipelines p ON p.WorkspaceId=d.WorkspaceId AND p.IsDefault=1;
            IF EXISTS (SELECT 1 FROM deals.Deals WHERE PipelineId IS NULL) THROW 51003, 'Deal pipeline backfill incomplete', 1;
            ALTER TABLE deals.Deals ALTER COLUMN PipelineId nvarchar(128) NOT NULL;
            CREATE INDEX IX_Deals_WorkspaceId_PipelineId_StageCode ON deals.Deals(WorkspaceId,PipelineId,StageCode);
            ALTER TABLE deals.Deals ADD CONSTRAINT FK_Deals_Pipelines_WorkspaceId_PipelineId
                FOREIGN KEY (WorkspaceId,PipelineId) REFERENCES deals.Pipelines(WorkspaceId,PipelineId);
            ALTER TABLE deals.Deals ADD CONSTRAINT FK_Deals_PipelineStages_Workspace_Pipeline_Code
                FOREIGN KEY (WorkspaceId,PipelineId,StageCode) REFERENCES deals.PipelineStages(WorkspaceId,PipelineId,Code);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(
        "THROW 51004, 'Pipeline configuration cannot be discarded by downgrade; use reviewed forward repair or restore a verified backup', 1;");
}
