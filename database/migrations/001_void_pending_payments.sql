-- GamingClub migration 001: support voiding unpaid invoices.
-- Select GamingClub or GamingClubTest_18 in SSMS. No GO (single fail-safe batch).
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF DB_NAME() NOT IN (N'GamingClub', N'GamingClubTest_18')
        THROW 51001, 'Select GamingClub or GamingClubTest_18 before migration 001.', 1;
    IF OBJECT_ID(N'dbo.Payments', N'U') IS NULL
        THROW 51002, 'dbo.Payments table does not exist.', 1;

    IF EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name=N'CK_Payments_Status'
                 AND parent_object_id=OBJECT_ID(N'dbo.Payments')
                 AND definition NOT LIKE N'%VOID%')
        ALTER TABLE dbo.Payments DROP CONSTRAINT CK_Payments_Status;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                   WHERE name=N'CK_Payments_Status'
                     AND parent_object_id=OBJECT_ID(N'dbo.Payments'))
        ALTER TABLE dbo.Payments WITH CHECK ADD CONSTRAINT CK_Payments_Status
            CHECK (PaymentStatus IN ('PENDING','PAID','REFUNDED','VOID'));
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
-- VOID marks an unpaid invoice as annulled; not a refund.
