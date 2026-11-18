-- GamingClub migration 001: allow voiding unpaid invoices on cancellation.
-- Select either GamingClub or GamingClubTest_18 in SSMS first.
-- This file is idempotent and refuses all other databases.
IF DB_NAME() NOT IN (N'GamingClub', N'GamingClubTest_18')
    THROW 51001, 'Select GamingClub or GamingClubTest_18 before migration 001.', 1;
GO
IF OBJECT_ID(N'dbo.Payments', N'U') IS NULL
    THROW 51002, 'dbo.Payments table does not exist.', 1;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name=N'CK_Payments_Status'
             AND parent_object_id=OBJECT_ID(N'dbo.Payments')
             AND definition NOT LIKE N'%VOID%')
    ALTER TABLE dbo.Payments DROP CONSTRAINT CK_Payments_Status;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name=N'CK_Payments_Status'
                 AND parent_object_id=OBJECT_ID(N'dbo.Payments'))
    ALTER TABLE dbo.Payments WITH CHECK ADD CONSTRAINT CK_Payments_Status
        CHECK (PaymentStatus IN ('PENDING','PAID','REFUNDED','VOID'));
GO
-- VOID means an invoice never paid, and is not proof of a financial refund.
