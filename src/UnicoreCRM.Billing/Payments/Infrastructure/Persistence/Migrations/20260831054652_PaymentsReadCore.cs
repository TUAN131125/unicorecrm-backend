using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Billing.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentsReadCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "PaymentIntents",
                schema: "payments",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PaymentIntentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    BuyerType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OrderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    InvoiceIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScheduleLineIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(38,6)", precision: 38, scale: 6, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    MethodCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CheckoutUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Purpose = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ResourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentIntents", x => new { x.WorkspaceId, x.PaymentIntentId });
                    table.CheckConstraint("CK_PaymentIntents_BuyerType", "(([BuyerType] COLLATE Latin1_General_100_BIN2 = N'CONTACT' AND DATALENGTH([BuyerType]) = DATALENGTH(N'CONTACT')) OR ([BuyerType] COLLATE Latin1_General_100_BIN2 = N'ORGANIZATION_ACCOUNT' AND DATALENGTH([BuyerType]) = DATALENGTH(N'ORGANIZATION_ACCOUNT')))");
                    table.CheckConstraint("CK_PaymentIntents_Currency", "[Currency] LIKE '[A-Z][A-Z][A-Z]' COLLATE Latin1_General_100_BIN2 AND DATALENGTH([Currency]) = 6");
                    table.CheckConstraint("CK_PaymentIntents_InvoiceIdsJson", "ISJSON([InvoiceIdsJson]) = 1");
                    table.CheckConstraint("CK_PaymentIntents_Purpose", "[Purpose] IS NULL OR ((([Purpose] COLLATE Latin1_General_100_BIN2 = N'DEPOSIT' AND DATALENGTH([Purpose]) = DATALENGTH(N'DEPOSIT')) OR ([Purpose] COLLATE Latin1_General_100_BIN2 = N'FULL_PAYMENT' AND DATALENGTH([Purpose]) = DATALENGTH(N'FULL_PAYMENT')) OR ([Purpose] COLLATE Latin1_General_100_BIN2 = N'INSTALLMENT' AND DATALENGTH([Purpose]) = DATALENGTH(N'INSTALLMENT')) OR ([Purpose] COLLATE Latin1_General_100_BIN2 = N'OVERDUE_REMINDER' AND DATALENGTH([Purpose]) = DATALENGTH(N'OVERDUE_REMINDER')) OR ([Purpose] COLLATE Latin1_General_100_BIN2 = N'OTHER' AND DATALENGTH([Purpose]) = DATALENGTH(N'OTHER'))))");
                    table.CheckConstraint("CK_PaymentIntents_ResourceVersion", "[ResourceVersion] >= 0");
                    table.CheckConstraint("CK_PaymentIntents_ScheduleLineIdsJson", "ISJSON([ScheduleLineIdsJson]) = 1");
                    table.CheckConstraint("CK_PaymentIntents_State", "(([State] COLLATE Latin1_General_100_BIN2 = N'CREATED' AND DATALENGTH([State]) = DATALENGTH(N'CREATED')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'REQUIRES_ACTION' AND DATALENGTH([State]) = DATALENGTH(N'REQUIRES_ACTION')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'PROCESSING' AND DATALENGTH([State]) = DATALENGTH(N'PROCESSING')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'SUCCEEDED' AND DATALENGTH([State]) = DATALENGTH(N'SUCCEEDED')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'FAILED' AND DATALENGTH([State]) = DATALENGTH(N'FAILED')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'CANCELLED' AND DATALENGTH([State]) = DATALENGTH(N'CANCELLED')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'EXPIRED' AND DATALENGTH([State]) = DATALENGTH(N'EXPIRED')))");
                });

            migrationBuilder.CreateTable(
                name: "PaymentPlans",
                schema: "payments",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PaymentPlanId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OrderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    BuyerType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    AgreementSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScheduleLineIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SupersedesPlanId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SupersededByPlanId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    EvidenceCount = table.Column<int>(type: "int", nullable: false),
                    ResourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentPlans", x => new { x.WorkspaceId, x.PaymentPlanId });
                    table.CheckConstraint("CK_PaymentPlans_AgreementSnapshotJson", "ISJSON([AgreementSnapshotJson]) = 1");
                    table.CheckConstraint("CK_PaymentPlans_BuyerType", "(([BuyerType] COLLATE Latin1_General_100_BIN2 = N'CONTACT' AND DATALENGTH([BuyerType]) = DATALENGTH(N'CONTACT')) OR ([BuyerType] COLLATE Latin1_General_100_BIN2 = N'ORGANIZATION_ACCOUNT' AND DATALENGTH([BuyerType]) = DATALENGTH(N'ORGANIZATION_ACCOUNT')))");
                    table.CheckConstraint("CK_PaymentPlans_Currency", "[Currency] LIKE '[A-Z][A-Z][A-Z]' COLLATE Latin1_General_100_BIN2 AND DATALENGTH([Currency]) = 6");
                    table.CheckConstraint("CK_PaymentPlans_EvidenceCount", "[EvidenceCount] >= 0");
                    table.CheckConstraint("CK_PaymentPlans_Kind", "(([Kind] COLLATE Latin1_General_100_BIN2 = N'FULL_PAYMENT' AND DATALENGTH([Kind]) = DATALENGTH(N'FULL_PAYMENT')) OR ([Kind] COLLATE Latin1_General_100_BIN2 = N'DEPOSIT_AND_BALANCE' AND DATALENGTH([Kind]) = DATALENGTH(N'DEPOSIT_AND_BALANCE')) OR ([Kind] COLLATE Latin1_General_100_BIN2 = N'INSTALLMENT' AND DATALENGTH([Kind]) = DATALENGTH(N'INSTALLMENT')) OR ([Kind] COLLATE Latin1_General_100_BIN2 = N'MILESTONE' AND DATALENGTH([Kind]) = DATALENGTH(N'MILESTONE')) OR ([Kind] COLLATE Latin1_General_100_BIN2 = N'CUSTOM' AND DATALENGTH([Kind]) = DATALENGTH(N'CUSTOM')))");
                    table.CheckConstraint("CK_PaymentPlans_ResourceVersion", "[ResourceVersion] >= 0");
                    table.CheckConstraint("CK_PaymentPlans_ScheduleLineIdsJson", "ISJSON([ScheduleLineIdsJson]) = 1");
                    table.CheckConstraint("CK_PaymentPlans_State", "(([State] COLLATE Latin1_General_100_BIN2 = N'DRAFT' AND DATALENGTH([State]) = DATALENGTH(N'DRAFT')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'ACTIVE' AND DATALENGTH([State]) = DATALENGTH(N'ACTIVE')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'SUPERSEDED' AND DATALENGTH([State]) = DATALENGTH(N'SUPERSEDED')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'CANCELLED' AND DATALENGTH([State]) = DATALENGTH(N'CANCELLED')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'COMPLETED' AND DATALENGTH([State]) = DATALENGTH(N'COMPLETED')))");
                });

            migrationBuilder.CreateTable(
                name: "PaymentRecords",
                schema: "payments",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PaymentRecordId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    BuyerType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OrderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PaymentIntentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(38,6)", precision: 38, scale: 6, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    MethodCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RefundOfPaymentRecordId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RefundOfCustomerCreditId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RefundIntentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ExternalReference = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: true),
                    EvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReconciliationState = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CodCustomerCollectionState = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    CodMerchantRemittanceState = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    EffectiveForReceivables = table.Column<bool>(type: "bit", nullable: false),
                    ResourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    AllocationsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RefundsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CustomerCreditsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UnallocatedAmount = table.Column<decimal>(type: "decimal(38,6)", precision: 38, scale: 6, nullable: false),
                    UnallocatedCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    RefundableAmount = table.Column<decimal>(type: "decimal(38,6)", precision: 38, scale: 6, nullable: false),
                    RefundableCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRecords", x => new { x.WorkspaceId, x.PaymentRecordId });
                    table.CheckConstraint("CK_PaymentRecords_AllocationsJson", "ISJSON([AllocationsJson]) = 1");
                    table.CheckConstraint("CK_PaymentRecords_BuyerType", "(([BuyerType] COLLATE Latin1_General_100_BIN2 = N'CONTACT' AND DATALENGTH([BuyerType]) = DATALENGTH(N'CONTACT')) OR ([BuyerType] COLLATE Latin1_General_100_BIN2 = N'ORGANIZATION_ACCOUNT' AND DATALENGTH([BuyerType]) = DATALENGTH(N'ORGANIZATION_ACCOUNT')))");
                    table.CheckConstraint("CK_PaymentRecords_Channel", "(([Channel] COLLATE Latin1_General_100_BIN2 = N'BANK' AND DATALENGTH([Channel]) = DATALENGTH(N'BANK')) OR ([Channel] COLLATE Latin1_General_100_BIN2 = N'ONLINE_GATEWAY' AND DATALENGTH([Channel]) = DATALENGTH(N'ONLINE_GATEWAY')) OR ([Channel] COLLATE Latin1_General_100_BIN2 = N'POS' AND DATALENGTH([Channel]) = DATALENGTH(N'POS')) OR ([Channel] COLLATE Latin1_General_100_BIN2 = N'CARRIER' AND DATALENGTH([Channel]) = DATALENGTH(N'CARRIER')) OR ([Channel] COLLATE Latin1_General_100_BIN2 = N'OFFLINE' AND DATALENGTH([Channel]) = DATALENGTH(N'OFFLINE')) OR ([Channel] COLLATE Latin1_General_100_BIN2 = N'EXTERNAL' AND DATALENGTH([Channel]) = DATALENGTH(N'EXTERNAL')))");
                    table.CheckConstraint("CK_PaymentRecords_CodCustomerCollectionState", "[CodCustomerCollectionState] IS NULL OR ((([CodCustomerCollectionState] COLLATE Latin1_General_100_BIN2 = N'NOT_REQUESTED' AND DATALENGTH([CodCustomerCollectionState]) = DATALENGTH(N'NOT_REQUESTED')) OR ([CodCustomerCollectionState] COLLATE Latin1_General_100_BIN2 = N'REQUESTED' AND DATALENGTH([CodCustomerCollectionState]) = DATALENGTH(N'REQUESTED')) OR ([CodCustomerCollectionState] COLLATE Latin1_General_100_BIN2 = N'COLLECTED' AND DATALENGTH([CodCustomerCollectionState]) = DATALENGTH(N'COLLECTED')) OR ([CodCustomerCollectionState] COLLATE Latin1_General_100_BIN2 = N'FAILED' AND DATALENGTH([CodCustomerCollectionState]) = DATALENGTH(N'FAILED'))))");
                    table.CheckConstraint("CK_PaymentRecords_CodMerchantRemittanceState", "[CodMerchantRemittanceState] IS NULL OR ((([CodMerchantRemittanceState] COLLATE Latin1_General_100_BIN2 = N'NOT_APPLICABLE' AND DATALENGTH([CodMerchantRemittanceState]) = DATALENGTH(N'NOT_APPLICABLE')) OR ([CodMerchantRemittanceState] COLLATE Latin1_General_100_BIN2 = N'PENDING' AND DATALENGTH([CodMerchantRemittanceState]) = DATALENGTH(N'PENDING')) OR ([CodMerchantRemittanceState] COLLATE Latin1_General_100_BIN2 = N'REMITTED' AND DATALENGTH([CodMerchantRemittanceState]) = DATALENGTH(N'REMITTED')) OR ([CodMerchantRemittanceState] COLLATE Latin1_General_100_BIN2 = N'FAILED' AND DATALENGTH([CodMerchantRemittanceState]) = DATALENGTH(N'FAILED'))))");
                    table.CheckConstraint("CK_PaymentRecords_Currency", "[Currency] LIKE '[A-Z][A-Z][A-Z]' COLLATE Latin1_General_100_BIN2 AND DATALENGTH([Currency]) = 6");
                    table.CheckConstraint("CK_PaymentRecords_CustomerCreditsJson", "ISJSON([CustomerCreditsJson]) = 1");
                    table.CheckConstraint("CK_PaymentRecords_EvidenceJson", "[EvidenceJson] IS NULL OR ISJSON([EvidenceJson]) = 1");
                    table.CheckConstraint("CK_PaymentRecords_Kind", "(([Kind] COLLATE Latin1_General_100_BIN2 = N'PAYMENT' AND DATALENGTH([Kind]) = DATALENGTH(N'PAYMENT')) OR ([Kind] COLLATE Latin1_General_100_BIN2 = N'REFUND' AND DATALENGTH([Kind]) = DATALENGTH(N'REFUND')))");
                    table.CheckConstraint("CK_PaymentRecords_ReconciliationState", "(([ReconciliationState] COLLATE Latin1_General_100_BIN2 = N'UNRECONCILED' AND DATALENGTH([ReconciliationState]) = DATALENGTH(N'UNRECONCILED')) OR ([ReconciliationState] COLLATE Latin1_General_100_BIN2 = N'MATCHED' AND DATALENGTH([ReconciliationState]) = DATALENGTH(N'MATCHED')) OR ([ReconciliationState] COLLATE Latin1_General_100_BIN2 = N'MISMATCH' AND DATALENGTH([ReconciliationState]) = DATALENGTH(N'MISMATCH')))");
                    table.CheckConstraint("CK_PaymentRecords_RefundableCurrency", "[RefundableCurrency] LIKE '[A-Z][A-Z][A-Z]' COLLATE Latin1_General_100_BIN2 AND DATALENGTH([RefundableCurrency]) = 6");
                    table.CheckConstraint("CK_PaymentRecords_RefundsJson", "ISJSON([RefundsJson]) = 1");
                    table.CheckConstraint("CK_PaymentRecords_ResourceVersion", "[ResourceVersion] >= 0");
                    table.CheckConstraint("CK_PaymentRecords_State", "(([State] COLLATE Latin1_General_100_BIN2 = N'CREATED' AND DATALENGTH([State]) = DATALENGTH(N'CREATED')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'PENDING' AND DATALENGTH([State]) = DATALENGTH(N'PENDING')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'PROCESSING' AND DATALENGTH([State]) = DATALENGTH(N'PROCESSING')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'SUCCEEDED' AND DATALENGTH([State]) = DATALENGTH(N'SUCCEEDED')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'FAILED' AND DATALENGTH([State]) = DATALENGTH(N'FAILED')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'CANCELLED' AND DATALENGTH([State]) = DATALENGTH(N'CANCELLED')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'EXPIRED' AND DATALENGTH([State]) = DATALENGTH(N'EXPIRED')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'REVERSED' AND DATALENGTH([State]) = DATALENGTH(N'REVERSED')))");
                    table.CheckConstraint("CK_PaymentRecords_UnallocatedCurrency", "[UnallocatedCurrency] LIKE '[A-Z][A-Z][A-Z]' COLLATE Latin1_General_100_BIN2 AND DATALENGTH([UnallocatedCurrency]) = 6");
                });

            migrationBuilder.CreateTable(
                name: "PaymentScheduleLines",
                schema: "payments",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PaymentScheduleLineId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PaymentPlanId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PaymentPlanVersion = table.Column<long>(type: "bigint", nullable: false),
                    OrderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    BuyerType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AmountRuleJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(38,6)", precision: 38, scale: 6, nullable: false),
                    AmountCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    DueRuleJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResolvedDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AllowedMethodCodesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreferredMethodCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    FulfillmentGate = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    InvoicePolicyCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SatisfiedAmount = table.Column<decimal>(type: "decimal(38,6)", precision: 38, scale: 6, nullable: false),
                    SatisfiedCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    OutstandingAmount = table.Column<decimal>(type: "decimal(38,6)", precision: 38, scale: 6, nullable: false),
                    OutstandingCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ResourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentScheduleLines", x => new { x.WorkspaceId, x.PaymentScheduleLineId });
                    table.CheckConstraint("CK_PaymentScheduleLines_AllowedMethodCodesJson", "ISJSON([AllowedMethodCodesJson]) = 1");
                    table.CheckConstraint("CK_PaymentScheduleLines_AmountRuleJson", "ISJSON([AmountRuleJson]) = 1");
                    table.CheckConstraint("CK_PaymentScheduleLines_BuyerType", "(([BuyerType] COLLATE Latin1_General_100_BIN2 = N'CONTACT' AND DATALENGTH([BuyerType]) = DATALENGTH(N'CONTACT')) OR ([BuyerType] COLLATE Latin1_General_100_BIN2 = N'ORGANIZATION_ACCOUNT' AND DATALENGTH([BuyerType]) = DATALENGTH(N'ORGANIZATION_ACCOUNT')))");
                    table.CheckConstraint("CK_PaymentScheduleLines_Channel", "[Channel] IS NULL OR ((([Channel] COLLATE Latin1_General_100_BIN2 = N'BANK' AND DATALENGTH([Channel]) = DATALENGTH(N'BANK')) OR ([Channel] COLLATE Latin1_General_100_BIN2 = N'ONLINE_GATEWAY' AND DATALENGTH([Channel]) = DATALENGTH(N'ONLINE_GATEWAY')) OR ([Channel] COLLATE Latin1_General_100_BIN2 = N'POS' AND DATALENGTH([Channel]) = DATALENGTH(N'POS')) OR ([Channel] COLLATE Latin1_General_100_BIN2 = N'CARRIER' AND DATALENGTH([Channel]) = DATALENGTH(N'CARRIER')) OR ([Channel] COLLATE Latin1_General_100_BIN2 = N'OFFLINE' AND DATALENGTH([Channel]) = DATALENGTH(N'OFFLINE')) OR ([Channel] COLLATE Latin1_General_100_BIN2 = N'EXTERNAL' AND DATALENGTH([Channel]) = DATALENGTH(N'EXTERNAL'))))");
                    table.CheckConstraint("CK_PaymentScheduleLines_DueRuleJson", "ISJSON([DueRuleJson]) = 1");
                    table.CheckConstraint("CK_PaymentScheduleLines_FulfillmentGate", "(([FulfillmentGate] COLLATE Latin1_General_100_BIN2 = N'NONE' AND DATALENGTH([FulfillmentGate]) = DATALENGTH(N'NONE')) OR ([FulfillmentGate] COLLATE Latin1_General_100_BIN2 = N'BEFORE_BOOKING' AND DATALENGTH([FulfillmentGate]) = DATALENGTH(N'BEFORE_BOOKING')) OR ([FulfillmentGate] COLLATE Latin1_General_100_BIN2 = N'BEFORE_DISPATCH' AND DATALENGTH([FulfillmentGate]) = DATALENGTH(N'BEFORE_DISPATCH')) OR ([FulfillmentGate] COLLATE Latin1_General_100_BIN2 = N'BEFORE_COMPLETION' AND DATALENGTH([FulfillmentGate]) = DATALENGTH(N'BEFORE_COMPLETION')))");
                    table.CheckConstraint("CK_PaymentScheduleLines_PlanVersion", "[PaymentPlanVersion] >= 0");
                    table.CheckConstraint("CK_PaymentScheduleLines_Purpose", "(([Purpose] COLLATE Latin1_General_100_BIN2 = N'FULL' AND DATALENGTH([Purpose]) = DATALENGTH(N'FULL')) OR ([Purpose] COLLATE Latin1_General_100_BIN2 = N'DEPOSIT' AND DATALENGTH([Purpose]) = DATALENGTH(N'DEPOSIT')) OR ([Purpose] COLLATE Latin1_General_100_BIN2 = N'BALANCE' AND DATALENGTH([Purpose]) = DATALENGTH(N'BALANCE')) OR ([Purpose] COLLATE Latin1_General_100_BIN2 = N'INSTALLMENT' AND DATALENGTH([Purpose]) = DATALENGTH(N'INSTALLMENT')) OR ([Purpose] COLLATE Latin1_General_100_BIN2 = N'MILESTONE' AND DATALENGTH([Purpose]) = DATALENGTH(N'MILESTONE')) OR ([Purpose] COLLATE Latin1_General_100_BIN2 = N'OTHER' AND DATALENGTH([Purpose]) = DATALENGTH(N'OTHER')))");
                    table.CheckConstraint("CK_PaymentScheduleLines_ResourceVersion", "[ResourceVersion] >= 0");
                    table.CheckConstraint("CK_PaymentScheduleLines_Sequence", "[Sequence] >= 1");
                    table.CheckConstraint("CK_PaymentScheduleLines_State", "(([State] COLLATE Latin1_General_100_BIN2 = N'SCHEDULED' AND DATALENGTH([State]) = DATALENGTH(N'SCHEDULED')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'NOT_DUE' AND DATALENGTH([State]) = DATALENGTH(N'NOT_DUE')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'DUE' AND DATALENGTH([State]) = DATALENGTH(N'DUE')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'PARTIAL' AND DATALENGTH([State]) = DATALENGTH(N'PARTIAL')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'SATISFIED' AND DATALENGTH([State]) = DATALENGTH(N'SATISFIED')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'OVERDUE' AND DATALENGTH([State]) = DATALENGTH(N'OVERDUE')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'VOIDED' AND DATALENGTH([State]) = DATALENGTH(N'VOIDED')))");
                });

            migrationBuilder.CreateTable(
                name: "ReadAuditRecords",
                schema: "payments",
                columns: table => new
                {
                    AuditId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ResourceVersion = table.Column<long>(type: "bigint", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadAuditRecords", x => x.AuditId);
                    table.CheckConstraint("CK_ReadAuditRecords_Outcome", "(([Outcome] COLLATE Latin1_General_100_BIN2 = N'READ' AND DATALENGTH([Outcome]) = DATALENGTH(N'READ')))");
                    table.CheckConstraint("CK_ReadAuditRecords_ResourceVersion", "[ResourceVersion] IS NULL OR [ResourceVersion] >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_WorkspaceId_OrderId",
                schema: "payments",
                table: "PaymentIntents",
                columns: new[] { "WorkspaceId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPlans_WorkspaceId_OrderId",
                schema: "payments",
                table: "PaymentPlans",
                columns: new[] { "WorkspaceId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRecords_WorkspaceId_BuyerId",
                schema: "payments",
                table: "PaymentRecords",
                columns: new[] { "WorkspaceId", "BuyerId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentScheduleLines_WorkspaceId_PaymentPlanId",
                schema: "payments",
                table: "PaymentScheduleLines",
                columns: new[] { "WorkspaceId", "PaymentPlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadAuditRecords_WorkspaceId_OccurredAt",
                schema: "payments",
                table: "ReadAuditRecords",
                columns: new[] { "WorkspaceId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentIntents",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "PaymentPlans",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "PaymentRecords",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "PaymentScheduleLines",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "ReadAuditRecords",
                schema: "payments");
        }
    }
}
