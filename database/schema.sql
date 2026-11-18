-- GamingClub, variant 18, SQL Server 2019+
-- Run in SSMS. Schema uses UTC timestamps; date intervals are [start, end).
IF DB_ID(N'GamingClub') IS NULL
    CREATE DATABASE GamingClub;
GO
USE GamingClub;
GO

CREATE TABLE dbo.Roles (
    RoleId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Code NVARCHAR(32) NOT NULL UNIQUE,
    DisplayName NVARCHAR(80) NOT NULL
);
GO
CREATE TABLE dbo.Users (
    UserId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    RoleId INT NOT NULL REFERENCES dbo.Roles(RoleId),
    Login NVARCHAR(80) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(255) NOT NULL,
    DisplayName NVARCHAR(100) NOT NULL,
    CreatedUtc DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
CREATE TABLE dbo.Halls (
    HallId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name NVARCHAR(80) NOT NULL UNIQUE
);
GO
CREATE TABLE dbo.Seats (
    SeatId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    HallId INT NOT NULL REFERENCES dbo.Halls(HallId),
    SeatNumber INT NOT NULL,
    PosX INT NOT NULL,
    PosY INT NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_Seats_Hall_Number UNIQUE (HallId, SeatNumber)
);
GO
CREATE TABLE dbo.Tariffs (
    TariffId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL UNIQUE,
    TariffType VARCHAR(20) NOT NULL,
    PriceRub DECIMAL(10,2) NOT NULL CHECK (PriceRub >= 0),
    PackageMinutes INT NULL,
    StartLocalTime TIME(0) NULL,
    EndLocalTime TIME(0) NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CONSTRAINT CK_Tariffs_Type CHECK (TariffType IN ('HOURLY','PACKAGE','NIGHT','HAPPY_HOURS')),
    CONSTRAINT CK_Tariffs_Package CHECK (
        (TariffType = 'PACKAGE' AND PackageMinutes IS NOT NULL AND PackageMinutes > 0)
        OR (TariffType <> 'PACKAGE' AND PackageMinutes IS NULL)
    ),
    CONSTRAINT CK_Tariffs_Window CHECK (
        (TariffType IN ('NIGHT','HAPPY_HOURS') AND StartLocalTime IS NOT NULL AND EndLocalTime IS NOT NULL
            AND StartLocalTime <> EndLocalTime)
        OR (TariffType IN ('HOURLY','PACKAGE') AND StartLocalTime IS NULL AND EndLocalTime IS NULL)
    )
);
GO
CREATE TABLE dbo.Bookings (
    BookingId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    UserId INT NOT NULL REFERENCES dbo.Users(UserId),
    SeatId INT NOT NULL REFERENCES dbo.Seats(SeatId),
    TariffId INT NOT NULL REFERENCES dbo.Tariffs(TariffId),
    StartUtc DATETIME2(0) NOT NULL,
    EndUtc DATETIME2(0) NOT NULL,
    Status VARCHAR(20) NOT NULL DEFAULT 'ACTIVE',
    CreatedUtc DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_Bookings_Interval CHECK (EndUtc > StartUtc),
    CONSTRAINT CK_Bookings_Status CHECK (Status IN ('ACTIVE','CANCELLED','COMPLETED'))
);
GO
CREATE INDEX IX_Bookings_Seat_Interval ON dbo.Bookings(SeatId, StartUtc, EndUtc) INCLUDE (Status);
GO
CREATE TABLE dbo.Sessions (
    SessionId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    BookingId INT NOT NULL UNIQUE REFERENCES dbo.Bookings(BookingId),
    StartedUtc DATETIME2(0) NOT NULL,
    EndsUtc DATETIME2(0) NOT NULL,
    FinishedUtc DATETIME2(0) NULL,
    CONSTRAINT CK_Sessions_Interval CHECK (EndsUtc > StartedUtc),
    CONSTRAINT CK_Sessions_Finished CHECK (FinishedUtc IS NULL OR FinishedUtc >= StartedUtc)
);
GO
CREATE TABLE dbo.Payments (
    PaymentId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    BookingId INT NOT NULL REFERENCES dbo.Bookings(BookingId),
    AmountRub DECIMAL(10,2) NOT NULL CHECK (AmountRub >= 0),
    PaidUtc DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    PaymentStatus VARCHAR(16) NOT NULL,
    CONSTRAINT CK_Payments_Status CHECK (PaymentStatus IN ('PENDING','PAID','REFUNDED'))
);
GO
-- WARNING: the index above does NOT itself prevent overlapping bookings.
-- Implement atomic conflict checking in the API inside a transaction with
-- suitable SQL Server locks / isolation; add integration tests for races.
